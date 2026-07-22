using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Contracts.Matches;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Matches;

/// <summary>
/// Ids of the matches immediately newer and older than the given match within the same team.
/// Ordering: newest first (by PlayedAt then Id).
/// </summary>
public sealed record GetAdjacentMatchIdsQuery(int TeamId, int MatchId) : IQuery<AdjacentMatchIdsDto>;

internal sealed class GetAdjacentMatchIdsQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<GetAdjacentMatchIdsQuery, AdjacentMatchIdsDto>
{
    public async Task<Result<AdjacentMatchIdsDto>> Handle(GetAdjacentMatchIdsQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberAsync(query.TeamId, cancellationToken))
            return Result.Failure<AdjacentMatchIdsDto>(CommonErrors.NotMember);

        var current = await db.Matches
            .AsNoTracking()
            .Where(m => m.Id == query.MatchId && m.TeamId == query.TeamId)
            .Select(m => new { m.Id, m.PlayedAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (current == null)
            return new AdjacentMatchIdsDto(null, null);

        var newerId = await db.Matches
            .AsNoTracking()
            .Where(m => m.TeamId == query.TeamId &&
                        (m.PlayedAt > current.PlayedAt ||
                         (m.PlayedAt == current.PlayedAt && m.Id > current.Id)))
            .OrderBy(m => m.PlayedAt).ThenBy(m => m.Id)
            .Select(m => (int?)m.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var olderId = await db.Matches
            .AsNoTracking()
            .Where(m => m.TeamId == query.TeamId &&
                        (m.PlayedAt < current.PlayedAt ||
                         (m.PlayedAt == current.PlayedAt && m.Id < current.Id)))
            .OrderByDescending(m => m.PlayedAt).ThenByDescending(m => m.Id)
            .Select(m => (int?)m.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return new AdjacentMatchIdsDto(newerId, olderId);
    }
}
