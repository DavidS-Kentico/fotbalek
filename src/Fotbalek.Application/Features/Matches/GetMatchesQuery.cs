using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Contracts.Matches;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Matches;

/// <summary>
/// One page of a team's match history, newest first. Optional filters: season and a
/// [From, ToExclusive) instant range (computed at the UI boundary from the user's local
/// period selection — see AI/architecture.md §3 on DateFilterHelper).
/// </summary>
public sealed record GetMatchesQuery(
    int TeamId,
    int Page = 1,
    int PageSize = Constants.Pagination.DefaultPageSize,
    int? SeasonId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? ToExclusive = null) : IQuery<List<MatchDto>>;

internal sealed class GetMatchesQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<GetMatchesQuery, List<MatchDto>>
{
    public async Task<Result<List<MatchDto>>> Handle(GetMatchesQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberAsync(query.TeamId, cancellationToken))
            return Result.Failure<List<MatchDto>>(CommonErrors.NotMember);

        var matches = db.Matches
            .AsNoTracking()
            .Where(m => m.TeamId == query.TeamId);
        if (query.SeasonId is int seasonId)
            matches = matches.Where(m => m.SeasonId == seasonId);
        if (query.From is { } from)
            matches = matches.Where(m => m.PlayedAt >= from);
        if (query.ToExclusive is { } to)
            matches = matches.Where(m => m.PlayedAt < to);

        var list = await matches
            .Include(m => m.MatchPlayers).ThenInclude(mp => mp.Player)
            .Include(m => m.Season)
            .OrderByDescending(m => m.PlayedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);
        return list.Select(m => m.ToDto()).ToList();
    }
}
