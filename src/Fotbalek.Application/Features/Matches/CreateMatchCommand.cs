using FluentValidation;
using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Features.Seasons;
using Fotbalek.Contracts.Matches;
using Fotbalek.Domain.Entities;
using Fotbalek.Domain.Services;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Matches;

/// <summary>
/// Records a 2v2 match and applies the ELO flow: all-time ratings always, plus the seasonal
/// ladder when <paramref name="Seasonal"/> and a season is active. Season resolution happens
/// under the season row lock (inside the behavior's transaction) — not from the form's stale
/// state; if the season ended between form load and submit the match falls back to off-season
/// and the caller is told via <c>SeasonalFallback</c>.
/// </summary>
public sealed record CreateMatchCommand(
    int TeamId,
    int Team1GoalkeeperId,
    int Team1AttackerId,
    int Team2GoalkeeperId,
    int Team2AttackerId,
    int Team1Score,
    int Team2Score,
    bool Seasonal) : ICommand<MatchCreationResultDto>;

internal sealed class CreateMatchCommandValidator : AbstractValidator<CreateMatchCommand>
{
    public CreateMatchCommandValidator()
    {
        RuleFor(c => c.Team1Score).InclusiveBetween(0, 10).WithMessage("Scores must be between 0 and 10.");
        RuleFor(c => c.Team2Score).InclusiveBetween(0, 10).WithMessage("Scores must be between 0 and 10.");
        RuleFor(c => c)
            .Must(c => c.Team1Score != c.Team2Score)
            .WithMessage("Scores cannot be equal (no draws allowed)")
            .Must(c => c.Team1Score == 10 || c.Team2Score == 10)
            .WithMessage("At least one team must score 10")
            .Must(c => new[] { c.Team1GoalkeeperId, c.Team1AttackerId, c.Team2GoalkeeperId, c.Team2AttackerId }
                .Distinct().Count() == 4)
            .WithMessage("All players must be different");
    }
}

internal sealed class CreateMatchCommandHandler(
    IAppDbContext db,
    IUserContext userContext,
    IDbLocks dbLocks)
    : ICommandHandler<CreateMatchCommand, MatchCreationResultDto>
{
    public async Task<Result<MatchCreationResultDto>> Handle(CreateMatchCommand command, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not int userId)
            return Result.Failure<MatchCreationResultDto>(CommonErrors.NotAuthenticated);

        // Authorization: captain OR one of the four players belongs to the user.
        int[] playerIds = [command.Team1GoalkeeperId, command.Team1AttackerId, command.Team2GoalkeeperId, command.Team2AttackerId];
        if (!await CanUserCreateMatchAsync(command.TeamId, userId, playerIds, cancellationToken))
            return Result.Failure<MatchCreationResultDto>(Error.Forbidden(
                "Matches.NotParticipant", "You can only create matches you participate in."));

        // Season resolution, player reads and ELO computation all happen inside the behavior's
        // transaction: seasonal match creation serializes per team through the season row lock,
        // and every read reflects the committed state at that point.
        Season? season = null;
        var seasonalFallback = false;
        if (command.Seasonal)
        {
            var probeNow = DateTimeOffset.UtcNow;
            var candidateId = await db.Seasons
                .Where(s => s.TeamId == command.TeamId)
                .Where(SeasonRules.ActiveAt(probeNow))
                .Select(s => (int?)s.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (candidateId is int seasonId)
            {
                await dbLocks.LockSeasonRowAsync(seasonId, cancellationToken);
                var locked = await db.Seasons.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == seasonId, cancellationToken);
                if (locked != null && locked.IsActiveAt(DateTimeOffset.UtcNow))
                {
                    season = locked;
                }
            }
            seasonalFallback = season == null;
        }

        // Get players and validate they belong to the team and are active.
        var players = await db.Players
            .Where(p => playerIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        if (players.Count != 4)
            return Result.Failure<MatchCreationResultDto>(Error.NotFound("Matches.PlayerNotFound", "Player not found"));

        var team1Gk = players[command.Team1GoalkeeperId];
        var team1Atk = players[command.Team1AttackerId];
        var team2Gk = players[command.Team2GoalkeeperId];
        var team2Atk = players[command.Team2AttackerId];

        if (players.Values.Any(p => p.TeamId != command.TeamId))
            return Result.Failure<MatchCreationResultDto>(Error.Validation(
                "Matches.WrongTeam", "All players must belong to the team"));
        if (players.Values.Any(p => !p.IsActive))
            return Result.Failure<MatchCreationResultDto>(Error.Validation(
                "Matches.InactivePlayer", "All players must be active"));

        // Matches are always recorded with PlayedAt = now — no backdating. Since closed seasons
        // always lie in the past, a new match can never land in a closed season.
        var now = DateTimeOffset.UtcNow;

        // Calculate all-time ELO — updated by every match, seasonal or not.
        var team1Elo = EloCalculator.GetTeamElo(team1Gk.Elo, team1Atk.Elo);
        var team2Elo = EloCalculator.GetTeamElo(team2Gk.Elo, team2Atk.Elo);
        var team1Won = command.Team1Score > command.Team2Score;
        var (change1, change2) = EloCalculator.CalculateEloChange(team1Elo, team2Elo, team1Won);

        var match = new Match
        {
            TeamId = command.TeamId,
            SeasonId = season?.Id,
            Season = season,
            Team1Score = command.Team1Score,
            Team2Score = command.Team2Score,
            PlayedAt = now,
            CreatedAt = now
        };
        db.Matches.Add(match);

        MatchPlayer[] matchPlayers =
        [
            CreateMatchPlayer(match, team1Gk, 1, Constants.Positions.Goalkeeper, change1),
            CreateMatchPlayer(match, team1Atk, 1, Constants.Positions.Attacker, change1),
            CreateMatchPlayer(match, team2Gk, 2, Constants.Positions.Goalkeeper, change2),
            CreateMatchPlayer(match, team2Atk, 2, Constants.Positions.Attacker, change2),
        ];
        db.MatchPlayers.AddRange(matchPlayers);

        // Update player ELOs
        team1Gk.Elo = EloCalculator.ApplyEloChange(team1Gk.Elo, change1);
        team1Atk.Elo = EloCalculator.ApplyEloChange(team1Atk.Elo, change1);
        team2Gk.Elo = EloCalculator.ApplyEloChange(team2Gk.Elo, change2);
        team2Atk.Elo = EloCalculator.ApplyEloChange(team2Atk.Elo, change2);

        if (season != null)
        {
            // Seasonal ladder: same ELO math, run once more against the seasonal ratings —
            // each ladder computes its own expected score from its own ratings.
            var ladder1Gk = await GetOrCreateSeasonPlayerAsync(season.Id, command.Team1GoalkeeperId, cancellationToken);
            var ladder1Atk = await GetOrCreateSeasonPlayerAsync(season.Id, command.Team1AttackerId, cancellationToken);
            var ladder2Gk = await GetOrCreateSeasonPlayerAsync(season.Id, command.Team2GoalkeeperId, cancellationToken);
            var ladder2Atk = await GetOrCreateSeasonPlayerAsync(season.Id, command.Team2AttackerId, cancellationToken);

            var seasonTeam1Elo = EloCalculator.GetTeamElo(ladder1Gk.Elo, ladder1Atk.Elo);
            var seasonTeam2Elo = EloCalculator.GetTeamElo(ladder2Gk.Elo, ladder2Atk.Elo);
            var (seasonChange1, seasonChange2) = EloCalculator.CalculateEloChange(seasonTeam1Elo, seasonTeam2Elo, team1Won);

            ApplySeasonElo(matchPlayers[0], ladder1Gk, seasonChange1);
            ApplySeasonElo(matchPlayers[1], ladder1Atk, seasonChange1);
            ApplySeasonElo(matchPlayers[2], ladder2Gk, seasonChange2);
            ApplySeasonElo(matchPlayers[3], ladder2Atk, seasonChange2);
        }

        await db.SaveChangesAsync(cancellationToken);

        // Load navigation for DTO mapping (players are already tracked).
        foreach (var mp in matchPlayers)
        {
            mp.Player = players[mp.PlayerId];
        }

        return new MatchCreationResultDto(match.ToDto(), season?.ToDto(), seasonalFallback);
    }

    private async Task<bool> CanUserCreateMatchAsync(int teamId, int userId, int[] playerIds, CancellationToken cancellationToken)
    {
        var team = await db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == teamId, cancellationToken);
        if (team == null) return false;
        if (team.CaptainUserId == userId) return true;

        return await db.Players
            .AsNoTracking()
            .AnyAsync(p => p.TeamId == teamId && p.UserId == userId && playerIds.Contains(p.Id), cancellationToken);
    }

    /// <summary>The SeasonPlayer ladder row, created lazily with the default rating on the player's first seasonal match.</summary>
    private async Task<SeasonPlayer> GetOrCreateSeasonPlayerAsync(int seasonId, int playerId, CancellationToken cancellationToken)
    {
        var seasonPlayer = await db.SeasonPlayers
            .FirstOrDefaultAsync(sp => sp.SeasonId == seasonId && sp.PlayerId == playerId, cancellationToken);
        if (seasonPlayer == null)
        {
            seasonPlayer = new SeasonPlayer { SeasonId = seasonId, PlayerId = playerId, Elo = Constants.Elo.DefaultRating };
            db.SeasonPlayers.Add(seasonPlayer);
        }
        return seasonPlayer;
    }

    private static void ApplySeasonElo(MatchPlayer matchPlayer, SeasonPlayer seasonPlayer, int change)
    {
        matchPlayer.SeasonEloBefore = seasonPlayer.Elo;
        seasonPlayer.Elo = EloCalculator.ApplyEloChange(seasonPlayer.Elo, change);
        matchPlayer.SeasonEloAfter = seasonPlayer.Elo;
        matchPlayer.SeasonEloChange = change;
    }

    private static MatchPlayer CreateMatchPlayer(Match match, Player player, int teamNumber, string position, int eloChange)
    {
        return new MatchPlayer
        {
            Match = match,
            PlayerId = player.Id,
            TeamNumber = teamNumber,
            Position = position,
            EloChange = eloChange,
            EloBefore = player.Elo,
            EloAfter = EloCalculator.ApplyEloChange(player.Elo, eloChange)
        };
    }
}
