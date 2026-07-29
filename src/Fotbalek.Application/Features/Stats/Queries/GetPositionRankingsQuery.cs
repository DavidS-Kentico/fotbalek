using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Contracts.Stats;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Stats.Queries;

/// <summary>All-time position tables (min games threshold; GK by fewest conceded/game, ATK by most
/// scored/game), ordered through the shared ladder chain — which adds the all-time ELO and PlayerId
/// tie-breaks the seasonal twin already had (see LadderLeaders).</summary>
public sealed record GetPositionRankingsQuery(int TeamId) : IQuery<PositionRankingsDto>;

internal sealed class GetPositionRankingsQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<GetPositionRankingsQuery, PositionRankingsDto>
{
    public async Task<Result<PositionRankingsDto>> Handle(GetPositionRankingsQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberAsync(query.TeamId, cancellationToken))
            return Result.Failure<PositionRankingsDto>(CommonErrors.NotMember);

        var players = await db.Players
            .AsNoTracking()
            .Where(p => p.TeamId == query.TeamId && p.IsActive)
            .ToListAsync(cancellationToken);

        if (players.Count == 0)
            return new PositionRankingsDto([], []);

        var playerIds = players.Select(p => p.Id).ToList();
        var allMatchPlayers = await db.MatchPlayers
            .AsNoTracking()
            .Where(mp => playerIds.Contains(mp.PlayerId))
            .Include(mp => mp.Match)
            .ToListAsync(cancellationToken);

        var matchPlayersByPlayer = allMatchPlayers
            .GroupBy(mp => mp.PlayerId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // The rows carry no ELO, so the tie-break chain needs it alongside them.
        var goalkeeperRows = new List<(PositionRanking Row, int Elo)>();
        var attackerRows = new List<(PositionRanking Row, int Elo)>();

        foreach (var player in players)
        {
            var matchPlayers = matchPlayersByPlayer.GetValueOrDefault(player.Id) ?? [];
            var stats = StatsCalculations.CalculateStreaksAndPositionStats(matchPlayers);

            if (LadderLeaders.IsPositionEligible(stats.GoalkeeperCount))
            {
                goalkeeperRows.Add((new PositionRanking
                {
                    PlayerId = player.Id,
                    PlayerName = player.Name,
                    AvatarId = player.AvatarId,
                    Games = stats.GoalkeeperCount,
                    Goals = stats.GoalsConcededAsGk,
                    AverageGoals = (double)stats.GoalsConcededAsGk / stats.GoalkeeperCount
                }, player.Elo));
            }

            if (LadderLeaders.IsPositionEligible(stats.AttackerCount))
            {
                attackerRows.Add((new PositionRanking
                {
                    PlayerId = player.Id,
                    PlayerName = player.Name,
                    AvatarId = player.AvatarId,
                    Games = stats.AttackerCount,
                    Goals = stats.GoalsScoredAsAtk,
                    AverageGoals = (double)stats.GoalsScoredAsAtk / stats.AttackerCount
                }, player.Elo));
            }
        }

        // Conceded per game asc / scored per game desc → games desc → ELO desc → PlayerId asc.
        var goalkeepers = goalkeeperRows
            .OrderGoalkeepers(x => new LadderLeaders.PositionKey(x.Row.AverageGoals, x.Row.Games, x.Elo, x.Row.PlayerId))
            .Select(x => x.Row)
            .ToList();
        var rank = 1;
        foreach (var gk in goalkeepers) gk.Rank = rank++;

        var attackers = attackerRows
            .OrderAttackers(x => new LadderLeaders.PositionKey(x.Row.AverageGoals, x.Row.Games, x.Elo, x.Row.PlayerId))
            .Select(x => x.Row)
            .ToList();
        rank = 1;
        foreach (var atk in attackers) atk.Rank = rank++;

        return new PositionRankingsDto(goalkeepers, attackers);
    }
}
