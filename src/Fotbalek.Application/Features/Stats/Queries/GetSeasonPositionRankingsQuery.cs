using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Contracts.Stats;
using Fotbalek.Domain.Services;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Stats.Queries;

/// <summary>
/// Season position tables — the same goals-per-game metric and thresholds as the awards, with
/// the award tie-break chain, so the podium always matches what the tables show. Frozen data
/// for closed seasons, on-the-fly aggregation for the active one.
/// </summary>
public sealed record GetSeasonPositionRankingsQuery(int SeasonId) : IQuery<PositionRankingsDto>;

internal sealed class GetSeasonPositionRankingsQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<GetSeasonPositionRankingsQuery, PositionRankingsDto>
{
    public async Task<Result<PositionRankingsDto>> Handle(GetSeasonPositionRankingsQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberOfSeasonTeamAsync(query.SeasonId, cancellationToken))
            return Result.Failure<PositionRankingsDto>(CommonErrors.NotMember);

        var season = await db.Seasons.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == query.SeasonId, cancellationToken);
        if (season is null)
            return new PositionRankingsDto([], []);

        List<(int PlayerId, string Name, int AvatarId, int Elo, SeasonAggregates.ParticipantAggregate Agg)> participants;

        if (season.IsClosed)
        {
            var frozen = await db.SeasonPlayerResults
                .Where(r => r.SeasonPlayer.SeasonId == season.Id && r.FinalRank != null)
                .Select(r => new
                {
                    r.SeasonPlayer.PlayerId,
                    r.SeasonPlayer.Player.Name,
                    r.SeasonPlayer.Player.AvatarId,
                    r.SeasonPlayer.Elo,
                    r.GoalkeeperMatches,
                    r.GoalsConcededAsGoalkeeper,
                    r.AttackerMatches,
                    r.GoalsScoredAsAttacker
                })
                .ToListAsync(cancellationToken);

            participants = frozen.Select(r =>
            {
                var agg = new SeasonAggregates.ParticipantAggregate
                {
                    GoalkeeperMatches = r.GoalkeeperMatches,
                    GoalsConcededAsGoalkeeper = r.GoalsConcededAsGoalkeeper,
                    AttackerMatches = r.AttackerMatches,
                    GoalsScoredAsAttacker = r.GoalsScoredAsAttacker
                };
                return (r.PlayerId, r.Name, r.AvatarId, r.Elo, agg);
            }).ToList();
        }
        else
        {
            var (ladder, aggregates) = await SeasonAggregateLoader.LoadLiveAsync(db, season.Id, cancellationToken);
            participants = ladder
                .Where(sp => sp.Player.IsActive && aggregates.ContainsKey(sp.PlayerId))
                .Select(sp => (sp.PlayerId, sp.Player.Name, sp.Player.AvatarId, sp.Elo, aggregates[sp.PlayerId]))
                .ToList();
        }

        var minGames = Constants.TimeThresholds.MinGamesForPositionBadge;

        // Goals per game (conceded asc / scored desc) → matches in position desc → seasonal ELO desc → PlayerId asc.
        var goalkeepers = participants
            .Where(p => p.Agg.GoalkeeperMatches >= minGames)
            .OrderBy(p => (double)p.Agg.GoalsConcededAsGoalkeeper / p.Agg.GoalkeeperMatches)
            .ThenByDescending(p => p.Agg.GoalkeeperMatches)
            .ThenByDescending(p => p.Elo)
            .ThenBy(p => p.PlayerId)
            .Select((p, index) => new PositionRanking
            {
                Rank = index + 1,
                PlayerId = p.PlayerId,
                PlayerName = p.Name,
                AvatarId = p.AvatarId,
                Games = p.Agg.GoalkeeperMatches,
                Goals = p.Agg.GoalsConcededAsGoalkeeper,
                AverageGoals = (double)p.Agg.GoalsConcededAsGoalkeeper / p.Agg.GoalkeeperMatches
            })
            .ToList();

        var attackers = participants
            .Where(p => p.Agg.AttackerMatches >= minGames)
            .OrderByDescending(p => (double)p.Agg.GoalsScoredAsAttacker / p.Agg.AttackerMatches)
            .ThenByDescending(p => p.Agg.AttackerMatches)
            .ThenByDescending(p => p.Elo)
            .ThenBy(p => p.PlayerId)
            .Select((p, index) => new PositionRanking
            {
                Rank = index + 1,
                PlayerId = p.PlayerId,
                PlayerName = p.Name,
                AvatarId = p.AvatarId,
                Games = p.Agg.AttackerMatches,
                Goals = p.Agg.GoalsScoredAsAttacker,
                AverageGoals = (double)p.Agg.GoalsScoredAsAttacker / p.Agg.AttackerMatches
            })
            .ToList();

        return new PositionRankingsDto(goalkeepers, attackers);
    }
}
