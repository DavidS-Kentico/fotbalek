using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Contracts.Stats;
using Fotbalek.SharedKernel;

namespace Fotbalek.Application.Features.Stats.Queries;

/// <summary>
/// The team's stat/badge results. Season scope when <paramref name="SeasonId"/> is set (badges
/// read the seasonal ladder — computed on the fly even for closed seasons, whose matches are
/// immutable). An optional [From, ToExclusive) instant range narrows the matches further
/// (computed at the UI boundary from the user's local period selection); a narrowed scope
/// hides full-scope-only stats.
/// </summary>
public sealed record GetTeamStatsQuery(
    int TeamId,
    int? SeasonId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? ToExclusive = null) : IQuery<List<StatResult>>;

internal sealed class GetTeamStatsQueryHandler(StatsEngine engine, TeamAccess teamAccess)
    : IQueryHandler<GetTeamStatsQuery, List<StatResult>>
{
    public async Task<Result<List<StatResult>>> Handle(GetTeamStatsQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberAsync(query.TeamId, cancellationToken))
            return Result.Failure<List<StatResult>>(CommonErrors.NotMember);

        var isFullScope = query.From is null && query.ToExclusive is null;

        if (query.SeasonId is int seasonId)
        {
            var (players, matches, seasonPlayers) = await engine.LoadSeasonAsync(query.TeamId, seasonId);
            var filtered = Filter(matches, query.From, query.ToExclusive);
            return engine.ComputeSeason(players, seasonPlayers, filtered, isFullScope);
        }
        else
        {
            var (players, matches) = await engine.LoadAsync(query.TeamId);
            var filtered = Filter(matches, query.From, query.ToExclusive);
            return engine.Compute(players, filtered, isFullScope);
        }
    }

    private static IReadOnlyList<Domain.Entities.Match> Filter(
        IReadOnlyList<Domain.Entities.Match> matches, DateTimeOffset? from, DateTimeOffset? toExclusive)
    {
        if (from is null && toExclusive is null)
            return matches;
        return matches
            .Where(m => (from is null || m.PlayedAt >= from) && (toExclusive is null || m.PlayedAt < toExclusive))
            .ToList();
    }
}
