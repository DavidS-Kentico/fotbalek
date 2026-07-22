using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Contracts.Stats;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Stats.Queries;

/// <summary>All-time position tables (min games threshold; GK by fewest conceded/game, ATK by most scored/game).</summary>
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

        var goalkeepers = new List<PositionRanking>();
        var attackers = new List<PositionRanking>();
        var minGames = Constants.TimeThresholds.MinGamesForPositionBadge;

        foreach (var player in players)
        {
            var matchPlayers = matchPlayersByPlayer.GetValueOrDefault(player.Id) ?? [];
            var stats = StatsCalculations.CalculateStreaksAndPositionStats(matchPlayers);

            if (stats.GoalkeeperCount >= minGames)
            {
                goalkeepers.Add(new PositionRanking
                {
                    PlayerId = player.Id,
                    PlayerName = player.Name,
                    AvatarId = player.AvatarId,
                    Games = stats.GoalkeeperCount,
                    Goals = stats.GoalsConcededAsGk,
                    AverageGoals = (double)stats.GoalsConcededAsGk / stats.GoalkeeperCount
                });
            }

            if (stats.AttackerCount >= minGames)
            {
                attackers.Add(new PositionRanking
                {
                    PlayerId = player.Id,
                    PlayerName = player.Name,
                    AvatarId = player.AvatarId,
                    Games = stats.AttackerCount,
                    Goals = stats.GoalsScoredAsAtk,
                    AverageGoals = (double)stats.GoalsScoredAsAtk / stats.AttackerCount
                });
            }
        }

        // Goalkeepers ranked by lowest goals conceded per game.
        goalkeepers = goalkeepers.OrderBy(g => g.AverageGoals).ThenByDescending(g => g.Games).ToList();
        var rank = 1;
        foreach (var gk in goalkeepers) gk.Rank = rank++;

        // Attackers ranked by highest goals scored per game.
        attackers = attackers.OrderByDescending(a => a.AverageGoals).ThenByDescending(a => a.Games).ToList();
        rank = 1;
        foreach (var atk in attackers) atk.Rank = rank++;

        return new PositionRankingsDto(goalkeepers, attackers);
    }
}
