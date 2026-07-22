using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Matches;

public sealed record GetMatchCountQuery(int TeamId, int? SeasonId = null) : IQuery<int>;

internal sealed class GetMatchCountQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<GetMatchCountQuery, int>
{
    public async Task<Result<int>> Handle(GetMatchCountQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberAsync(query.TeamId, cancellationToken))
            return Result.Failure<int>(CommonErrors.NotMember);

        return await db.Matches.CountAsync(
            m => m.TeamId == query.TeamId && (query.SeasonId == null || m.SeasonId == query.SeasonId),
            cancellationToken);
    }
}
