using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Seasons;

public sealed record GetSeasonMatchCountQuery(int SeasonId) : IQuery<int>;

internal sealed class GetSeasonMatchCountQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<GetSeasonMatchCountQuery, int>
{
    public async Task<Result<int>> Handle(GetSeasonMatchCountQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberOfSeasonTeamAsync(query.SeasonId, cancellationToken))
            return Result.Failure<int>(CommonErrors.NotMember);

        return await db.Matches.CountAsync(m => m.SeasonId == query.SeasonId, cancellationToken);
    }
}
