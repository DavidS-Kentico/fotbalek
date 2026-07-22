using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Contracts.Seasons;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Seasons;

public sealed record GetSeasonQuery(int SeasonId) : IQuery<SeasonDto?>;

internal sealed class GetSeasonQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<GetSeasonQuery, SeasonDto?>
{
    public async Task<Result<SeasonDto?>> Handle(GetSeasonQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberOfSeasonTeamAsync(query.SeasonId, cancellationToken))
            return Result.Failure<SeasonDto?>(CommonErrors.NotMember);

        var season = await db.Seasons.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == query.SeasonId, cancellationToken);
        return season?.ToDto();
    }
}
