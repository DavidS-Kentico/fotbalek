using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Application.Features.Matches;
using Fotbalek.Contracts.Matches;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Seasons;

/// <summary>Unassigned matches of the team whose PlayedAt falls within the given period — the import list.</summary>
public sealed record GetImportCandidatesQuery(int TeamId, DateTimeOffset StartsAt, DateTimeOffset? EndsAt)
    : IQuery<List<MatchDto>>;

internal sealed class GetImportCandidatesQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<GetImportCandidatesQuery, List<MatchDto>>
{
    public async Task<Result<List<MatchDto>>> Handle(GetImportCandidatesQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberAsync(query.TeamId, cancellationToken))
            return Result.Failure<List<MatchDto>>(CommonErrors.NotMember);

        var matches = await db.Matches
            .AsNoTracking()
            .Where(m => m.TeamId == query.TeamId && m.SeasonId == null &&
                        m.PlayedAt >= query.StartsAt && (query.EndsAt == null || m.PlayedAt < query.EndsAt))
            .Include(m => m.MatchPlayers).ThenInclude(mp => mp.Player)
            .OrderBy(m => m.PlayedAt).ThenBy(m => m.Id)
            .ToListAsync(cancellationToken);
        return matches.Select(m => m.ToDto()).ToList();
    }
}
