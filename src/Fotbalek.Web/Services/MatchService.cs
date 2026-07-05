using Fotbalek.Web.Data;
using Fotbalek.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Web.Services;

public class MatchService(IDbContextFactory<AppDbContext> dbFactory, EloService eloService)
{
    /// <summary>
    /// Result of match creation. <paramref name="SeasonalFallback"/> is true when a seasonal match
    /// was requested but the season ended (or was closed) between form load and submit — the match
    /// was recorded off-season and the user should be notified.
    /// </summary>
    public sealed record MatchCreationResult(Match Match, Season? Season, bool SeasonalFallback);

    public async Task<List<Match>> GetByTeamAsync(int teamId, int page = 1, int pageSize = 20)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Matches
            .Where(m => m.TeamId == teamId)
            .Include(m => m.MatchPlayers)
                .ThenInclude(mp => mp.Player)
            .Include(m => m.Season)
            .OrderByDescending(m => m.PlayedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<int> GetCountByTeamAsync(int teamId, int? seasonId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Matches.CountAsync(m => m.TeamId == teamId &&
            (seasonId == null || m.SeasonId == seasonId));
    }

    public async Task<Match?> GetByIdAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await GetByIdAsync(db, id);
    }

    private static Task<Match?> GetByIdAsync(AppDbContext db, int id)
    {
        return db.Matches
            .Include(m => m.MatchPlayers)
                .ThenInclude(mp => mp.Player)
            .Include(m => m.Season)
            .FirstOrDefaultAsync(m => m.Id == id);
    }

    public async Task<MatchCreationResult> CreateAsync(
        int teamId,
        int team1GkId, int team1AtkId,
        int team2GkId, int team2AtkId,
        int team1Score, int team2Score,
        int userId,
        bool seasonal = false)
    {
        // Authorization: admin OR one of the four players belongs to the user.
        var playerIds = new[] { team1GkId, team1AtkId, team2GkId, team2AtkId };
        if (!await CanUserCreateMatchAsync(teamId, userId, playerIds))
            throw new UnauthorizedAccessException("You can only create matches you participate in.");


        // Validate scores
        if (team1Score < 0 || team2Score < 0)
            throw new ArgumentException("Scores cannot be negative");
        if (team1Score > 10 || team2Score > 10)
            throw new ArgumentException("Scores cannot exceed 10");
        if (team1Score == team2Score)
            throw new ArgumentException("Scores cannot be equal (no draws allowed)");
        if (team1Score != 10 && team2Score != 10)
            throw new ArgumentException("At least one team must score 10");

        // Validate all players are unique
        if (playerIds.Distinct().Count() != 4)
            throw new ArgumentException("All players must be different");

        // Everything below — season resolution, player reads, ELO computation — happens on a fresh
        // context inside the transaction: seasonal match creation serializes per team through the
        // season update lock, and every read reflects the committed state at that point.
        await using var db = await dbFactory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            // Resolve the active season under the season update lock — not from the form's stale
            // state. If the season ended (or was closed) between form load and submit, fall back
            // to off-season and tell the caller.
            Season? season = null;
            var seasonalFallback = false;
            if (seasonal)
            {
                var probeNow = DateTimeOffset.UtcNow;
                var candidateId = await db.Seasons
                    .Where(s => s.TeamId == teamId && s.ClosedAt == null &&
                                s.StartsAt <= probeNow && (s.EndsAt == null || probeNow < s.EndsAt))
                    .Select(s => (int?)s.Id)
                    .FirstOrDefaultAsync();
                if (candidateId is int seasonId)
                {
                    await SeasonService.LockSeasonRowAsync(db, seasonId);
                    var locked = await db.Seasons.AsNoTracking().FirstOrDefaultAsync(s => s.Id == seasonId);
                    if (locked != null && locked.IsActiveAt(DateTimeOffset.UtcNow))
                    {
                        season = locked;
                    }
                }
                seasonalFallback = season == null;
            }

            // Get players and validate they belong to the team and are active
            var team1Gk = await db.Players.FindAsync(team1GkId) ?? throw new InvalidOperationException("Player not found");
            var team1Atk = await db.Players.FindAsync(team1AtkId) ?? throw new InvalidOperationException("Player not found");
            var team2Gk = await db.Players.FindAsync(team2GkId) ?? throw new InvalidOperationException("Player not found");
            var team2Atk = await db.Players.FindAsync(team2AtkId) ?? throw new InvalidOperationException("Player not found");

            // Validate all players belong to the same team
            if (team1Gk.TeamId != teamId || team1Atk.TeamId != teamId ||
                team2Gk.TeamId != teamId || team2Atk.TeamId != teamId)
                throw new ArgumentException("All players must belong to the team");

            // Validate all players are active
            if (!team1Gk.IsActive || !team1Atk.IsActive || !team2Gk.IsActive || !team2Atk.IsActive)
                throw new ArgumentException("All players must be active");

            // Matches are always recorded with PlayedAt = now — no backdating. Since closed seasons
            // always lie in the past, a new match can never land in a closed season.
            var now = DateTimeOffset.UtcNow;

            // Calculate all-time ELO — updated by every match, seasonal or not.
            var team1Elo = eloService.GetTeamElo(team1Gk.Elo, team1Atk.Elo);
            var team2Elo = eloService.GetTeamElo(team2Gk.Elo, team2Atk.Elo);
            var team1Won = team1Score > team2Score;
            var (change1, change2) = eloService.CalculateEloChange(team1Elo, team2Elo, team1Won);

            // Create match
            var match = new Match
            {
                TeamId = teamId,
                SeasonId = season?.Id,
                Team1Score = team1Score,
                Team2Score = team2Score,
                PlayedAt = now,
                CreatedAt = now
            };

            db.Matches.Add(match);

            // Create match players with ELO tracking
            var matchPlayers = new[]
            {
                CreateMatchPlayer(match, team1Gk, 1, Constants.Positions.Goalkeeper, change1),
                CreateMatchPlayer(match, team1Atk, 1, Constants.Positions.Attacker, change1),
                CreateMatchPlayer(match, team2Gk, 2, Constants.Positions.Goalkeeper, change2),
                CreateMatchPlayer(match, team2Atk, 2, Constants.Positions.Attacker, change2),
            };

            db.MatchPlayers.AddRange(matchPlayers);

            // Update player ELOs
            team1Gk.Elo = eloService.ApplyEloChange(team1Gk.Elo, change1);
            team1Atk.Elo = eloService.ApplyEloChange(team1Atk.Elo, change1);
            team2Gk.Elo = eloService.ApplyEloChange(team2Gk.Elo, change2);
            team2Atk.Elo = eloService.ApplyEloChange(team2Atk.Elo, change2);

            if (season != null)
            {
                // Seasonal ladder: same ELO math, run once more against the seasonal ratings —
                // each ladder computes its own expected score from its own ratings.
                var ladder1Gk = await GetOrCreateSeasonPlayerAsync(db, season.Id, team1GkId);
                var ladder1Atk = await GetOrCreateSeasonPlayerAsync(db, season.Id, team1AtkId);
                var ladder2Gk = await GetOrCreateSeasonPlayerAsync(db, season.Id, team2GkId);
                var ladder2Atk = await GetOrCreateSeasonPlayerAsync(db, season.Id, team2AtkId);

                var seasonTeam1Elo = eloService.GetTeamElo(ladder1Gk.Elo, ladder1Atk.Elo);
                var seasonTeam2Elo = eloService.GetTeamElo(ladder2Gk.Elo, ladder2Atk.Elo);
                var (seasonChange1, seasonChange2) = eloService.CalculateEloChange(seasonTeam1Elo, seasonTeam2Elo, team1Won);

                ApplySeasonElo(matchPlayers[0], ladder1Gk, seasonChange1);
                ApplySeasonElo(matchPlayers[1], ladder1Atk, seasonChange1);
                ApplySeasonElo(matchPlayers[2], ladder2Gk, seasonChange2);
                ApplySeasonElo(matchPlayers[3], ladder2Atk, seasonChange2);
            }

            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            return new MatchCreationResult(match, season, seasonalFallback);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>The SeasonPlayer ladder row, created lazily with the default rating on the player's first seasonal match.</summary>
    private static async Task<SeasonPlayer> GetOrCreateSeasonPlayerAsync(AppDbContext db, int seasonId, int playerId)
    {
        var seasonPlayer = await db.SeasonPlayers
            .FirstOrDefaultAsync(sp => sp.SeasonId == seasonId && sp.PlayerId == playerId);
        if (seasonPlayer == null)
        {
            seasonPlayer = new SeasonPlayer { SeasonId = seasonId, PlayerId = playerId, Elo = Constants.Elo.DefaultRating };
            db.SeasonPlayers.Add(seasonPlayer);
        }
        return seasonPlayer;
    }

    private void ApplySeasonElo(MatchPlayer matchPlayer, SeasonPlayer seasonPlayer, int change)
    {
        matchPlayer.SeasonEloBefore = seasonPlayer.Elo;
        seasonPlayer.Elo = eloService.ApplyEloChange(seasonPlayer.Elo, change);
        matchPlayer.SeasonEloAfter = seasonPlayer.Elo;
        matchPlayer.SeasonEloChange = change;
    }

    private MatchPlayer CreateMatchPlayer(Match match, Player player, int teamNumber, string position, int eloChange)
    {
        return new MatchPlayer
        {
            Match = match,
            PlayerId = player.Id,
            TeamNumber = teamNumber,
            Position = position,
            EloChange = eloChange,
            EloBefore = player.Elo,
            EloAfter = eloService.ApplyEloChange(player.Elo, eloChange)
        };
    }

    /// <summary>
    /// Returns true if the current user can delete/edit this match:
    /// - the time window and "no later matches" rules are satisfied, AND
    /// - the user is the team admin OR has a Player participating in the match.
    /// </summary>
    public async Task<bool> CanUserEditOrDeleteAsync(int matchId, int teamId, int userId)
    {
        var (canDelete, _) = await CanDeleteWithReasonAsync(matchId);
        if (!canDelete) return false;

        await using var db = await dbFactory.CreateDbContextAsync();
        var team = await db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == teamId);
        if (team == null) return false;
        if (team.AdminUserId == userId) return true;

        var hasPlayerInMatch = await db.MatchPlayers
            .AsNoTracking()
            .AnyAsync(mp => mp.MatchId == matchId && mp.Player.UserId == userId);
        return hasPlayerInMatch;
    }

    /// <summary>
    /// Returns true if user is admin of the team OR one of the player ids belongs to the user.
    /// Used to gate match creation.
    /// </summary>
    public async Task<bool> CanUserCreateMatchAsync(int teamId, int userId, IEnumerable<int> playerIds)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var team = await db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == teamId);
        if (team == null) return false;
        if (team.AdminUserId == userId) return true;

        var ids = playerIds.ToList();
        return await db.Players
            .AsNoTracking()
            .AnyAsync(p => p.TeamId == teamId && p.UserId == userId && ids.Contains(p.Id));
    }

    public async Task<(bool CanDelete, string? Reason)> CanDeleteWithReasonAsync(int matchId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var match = await db.Matches
            .Include(m => m.MatchPlayers)
            .FirstOrDefaultAsync(m => m.Id == matchId);
        if (match == null) return (false, "Match not found");

        var hoursSinceCreation = (DateTimeOffset.UtcNow - match.CreatedAt).TotalHours;
        if (hoursSinceCreation > Constants.TimeThresholds.MatchDeletionWindowHours)
            return (false, $"Matches can only be deleted within {Constants.TimeThresholds.MatchDeletionWindowHours} hours of creation");

        // Matches of a closed season cannot be deleted — deleting would corrupt frozen standings
        // and awards. Reachable when the admin ends a season prematurely inside the 24h window.
        if (match.SeasonId != null &&
            await db.Seasons.AnyAsync(s => s.Id == match.SeasonId && s.ClosedAt != null))
            return (false, "This match belongs to a closed season — its results are frozen");

        // Check if this is the most recent match for all players involved
        // This ensures ELO reversal won't corrupt subsequent match history
        // We use MatchId for comparison since matches are always created with current time
        foreach (var mp in match.MatchPlayers)
        {
            var hasLaterMatch = await db.MatchPlayers
                .AnyAsync(laterMp =>
                    laterMp.PlayerId == mp.PlayerId &&
                    laterMp.MatchId > match.Id);

            if (hasLaterMatch)
                return (false, "Cannot delete: one or more players have played matches after this one");
        }

        return (true, null);
    }

    public async Task<bool> DeleteAsync(int matchId, int teamId, int userId)
    {
        // Re-check permissions right before deleting — the caller's button state may be stale.
        if (!await CanUserEditOrDeleteAsync(matchId, teamId, userId))
            return false;

        await using var db = await dbFactory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var match = await GetByIdAsync(db, matchId);
            if (match == null) return false;

            // Validate match belongs to the authenticated team
            if (match.TeamId != teamId)
                return false;

            // For a seasonal match, re-verify under the season update lock — the check cannot race
            // a concurrent close, and a concurrent EndsAt shrink may have unassigned the match.
            if (match.SeasonId is int seasonId)
            {
                await SeasonService.LockSeasonRowAsync(db, seasonId);
                await db.Entry(match).ReloadAsync();
                if (match.SeasonId == seasonId)
                {
                    var stillOpen = await db.Seasons.AnyAsync(s => s.Id == seasonId && s.ClosedAt == null);
                    if (!stillOpen) return false;
                }
            }

            // Reverse ELO changes
            foreach (var mp in match.MatchPlayers)
            {
                var player = await db.Players.FindAsync(mp.PlayerId);
                if (player != null)
                {
                    player.Elo = mp.EloBefore;
                }
            }

            // Seasonal ELO is reversed too, in the same transaction. The existing "no participant
            // has a later match" guard is a superset of the seasonal requirement, so no additional
            // ordering check is needed.
            if (match.SeasonId is int sid)
            {
                var participantIds = match.MatchPlayers.Select(mp => mp.PlayerId).ToList();
                var ladderRows = await db.SeasonPlayers
                    .Where(sp => sp.SeasonId == sid && participantIds.Contains(sp.PlayerId))
                    .ToListAsync();

                foreach (var mp in match.MatchPlayers)
                {
                    var ladderRow = ladderRows.FirstOrDefault(sp => sp.PlayerId == mp.PlayerId);
                    if (ladderRow != null && mp.SeasonEloBefore is int seasonEloBefore)
                    {
                        ladderRow.Elo = seasonEloBefore;
                    }
                }

                // A SeasonPlayer left with zero season matches is deleted.
                foreach (var ladderRow in ladderRows)
                {
                    var hasOtherSeasonMatch = await db.MatchPlayers.AnyAsync(mp =>
                        mp.PlayerId == ladderRow.PlayerId &&
                        mp.MatchId != match.Id &&
                        mp.Match.SeasonId == sid);
                    if (!hasOtherSeasonMatch)
                    {
                        db.SeasonPlayers.Remove(ladderRow);
                    }
                }
            }

            db.Matches.Remove(match);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return true;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<List<Match>> GetRecentByTeamAsync(int teamId, int count = 10, int? seasonId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Matches
            .Where(m => m.TeamId == teamId && (seasonId == null || m.SeasonId == seasonId))
            .Include(m => m.MatchPlayers)
                .ThenInclude(mp => mp.Player)
            .OrderByDescending(m => m.PlayedAt)
            .Take(count)
            .ToListAsync();
    }

    public async Task<int> GetMatchesThisWeekAsync(int teamId, int? seasonId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var recentActivityThreshold = DateTimeOffset.UtcNow.AddDays(-Constants.TimeThresholds.RecentActivityDays);
        return await db.Matches
            .CountAsync(m => m.TeamId == teamId && m.PlayedAt >= recentActivityThreshold &&
                (seasonId == null || m.SeasonId == seasonId));
    }

    public async Task<double> GetAverageMatchScoreAsync(int teamId, int? seasonId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var matches = await db.Matches
            .Where(m => m.TeamId == teamId && (seasonId == null || m.SeasonId == seasonId))
            .Select(m => m.Team1Score + m.Team2Score)
            .ToListAsync();

        return matches.Count > 0 ? matches.Average() : 0;
    }

    public async Task<List<Match>> GetByPlayerAsync(int playerId, int count = 10, int? seasonId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Matches
            .Where(m => m.MatchPlayers.Any(mp => mp.PlayerId == playerId) &&
                (seasonId == null || m.SeasonId == seasonId))
            .Include(m => m.MatchPlayers)
                .ThenInclude(mp => mp.Player)
            .OrderByDescending(m => m.PlayedAt)
            .Take(count)
            .ToListAsync();
    }

    public async Task<int> GetCountByPlayerAsync(int playerId, int? seasonId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Matches
            .CountAsync(m => m.MatchPlayers.Any(mp => mp.PlayerId == playerId) &&
                (seasonId == null || m.SeasonId == seasonId));
    }

    /// <summary>
    /// Returns the IDs of the matches immediately newer and older than the given match within the same team.
    /// Ordering: newest first (by PlayedAt then Id).
    /// </summary>
    public async Task<(int? NewerId, int? OlderId)> GetAdjacentMatchIdsAsync(int matchId, int teamId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var current = await db.Matches
            .AsNoTracking()
            .Where(m => m.Id == matchId && m.TeamId == teamId)
            .Select(m => new { m.Id, m.PlayedAt })
            .FirstOrDefaultAsync();
        if (current == null) return (null, null);

        var newerId = await db.Matches
            .AsNoTracking()
            .Where(m => m.TeamId == teamId &&
                       (m.PlayedAt > current.PlayedAt ||
                        (m.PlayedAt == current.PlayedAt && m.Id > current.Id)))
            .OrderBy(m => m.PlayedAt).ThenBy(m => m.Id)
            .Select(m => (int?)m.Id)
            .FirstOrDefaultAsync();

        var olderId = await db.Matches
            .AsNoTracking()
            .Where(m => m.TeamId == teamId &&
                       (m.PlayedAt < current.PlayedAt ||
                        (m.PlayedAt == current.PlayedAt && m.Id < current.Id)))
            .OrderByDescending(m => m.PlayedAt).ThenByDescending(m => m.Id)
            .Select(m => (int?)m.Id)
            .FirstOrDefaultAsync();

        return (newerId, olderId);
    }
}
