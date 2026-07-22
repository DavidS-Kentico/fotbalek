using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Seasons;

/// <summary>Season ids of the team that are past their end date and not yet closed —
/// the host's lazy-close dispatches CloseSeasonCommand per id (AI/architecture.md §3).
/// Member-gated: the lazy close is triggered by a member's page load, after resolution.</summary>
public sealed record GetDueSeasonIdsQuery(int TeamId) : IQuery<List<int>>;

internal sealed class GetDueSeasonIdsQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<GetDueSeasonIdsQuery, List<int>>
{
    public async Task<Result<List<int>>> Handle(GetDueSeasonIdsQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberAsync(query.TeamId, cancellationToken))
            return Result.Failure<List<int>>(CommonErrors.NotMember);

        var now = DateTimeOffset.UtcNow;
        return await db.Seasons
            .Where(s => s.TeamId == query.TeamId && s.ClosedAt == null && s.EndsAt != null && s.EndsAt <= now)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);
    }
}
