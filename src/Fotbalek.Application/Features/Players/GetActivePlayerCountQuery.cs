using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Players;

public sealed record GetActivePlayerCountQuery(int TeamId) : IQuery<int>;

internal sealed class GetActivePlayerCountQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<GetActivePlayerCountQuery, int>
{
    public async Task<Result<int>> Handle(GetActivePlayerCountQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberAsync(query.TeamId, cancellationToken))
            return Result.Failure<int>(CommonErrors.NotMember);

        return await db.Players.CountAsync(p => p.TeamId == query.TeamId && p.IsActive, cancellationToken);
    }
}
