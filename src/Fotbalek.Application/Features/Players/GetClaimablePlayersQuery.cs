using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Contracts.Players;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Players;

/// <summary>Active placeholder players available to claim.</summary>
public sealed record GetClaimablePlayersQuery(int TeamId) : IQuery<List<PlayerDto>>;

internal sealed class GetClaimablePlayersQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<GetClaimablePlayersQuery, List<PlayerDto>>
{
    public async Task<Result<List<PlayerDto>>> Handle(GetClaimablePlayersQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberAsync(query.TeamId, cancellationToken))
            return Result.Failure<List<PlayerDto>>(CommonErrors.NotMember);

        var list = await db.Players.AsNoTracking()
            .Where(p => p.TeamId == query.TeamId && p.UserId == null && p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);
        return list.Select(p => p.ToDto()).ToList();
    }
}
