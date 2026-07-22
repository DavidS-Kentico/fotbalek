using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Seasons;

/// <summary>Whether the team has any seasons at all (pages fall back to all-time behavior when false).</summary>
public sealed record HasAnySeasonQuery(int TeamId) : IQuery<bool>;

internal sealed class HasAnySeasonQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<HasAnySeasonQuery, bool>
{
    public async Task<Result<bool>> Handle(HasAnySeasonQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberAsync(query.TeamId, cancellationToken))
            return Result.Failure<bool>(CommonErrors.NotMember);

        return await db.Seasons.AnyAsync(s => s.TeamId == query.TeamId, cancellationToken);
    }
}
