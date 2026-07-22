using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Contracts.Seasons;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Seasons;

/// <summary>The season currently accepting matches, or null. At most one exists per team.</summary>
public sealed record GetActiveSeasonQuery(int TeamId) : IQuery<SeasonDto?>;

internal sealed class GetActiveSeasonQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<GetActiveSeasonQuery, SeasonDto?>
{
    public async Task<Result<SeasonDto?>> Handle(GetActiveSeasonQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberAsync(query.TeamId, cancellationToken))
            return Result.Failure<SeasonDto?>(CommonErrors.NotMember);

        var now = DateTimeOffset.UtcNow;
        var season = await db.Seasons
            .AsNoTracking()
            .Where(s => s.TeamId == query.TeamId)
            .Where(SeasonRules.ActiveAt(now))
            .FirstOrDefaultAsync(cancellationToken);
        return season?.ToDto();
    }
}
