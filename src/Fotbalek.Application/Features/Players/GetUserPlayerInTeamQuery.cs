using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Contracts.Players;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Players;

/// <summary>A specific user's claimed player in a team — the GameHub join path (§4.4).
/// Gated on the CALLER's membership; any member may resolve a teammate's player.</summary>
public sealed record GetUserPlayerInTeamQuery(int TeamId, int UserId) : IQuery<PlayerDto?>;

internal sealed class GetUserPlayerInTeamQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<GetUserPlayerInTeamQuery, PlayerDto?>
{
    public async Task<Result<PlayerDto?>> Handle(GetUserPlayerInTeamQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberAsync(query.TeamId, cancellationToken))
            return Result.Failure<PlayerDto?>(CommonErrors.NotMember);

        var player = await db.Players.AsNoTracking()
            .FirstOrDefaultAsync(p => p.TeamId == query.TeamId && p.UserId == query.UserId, cancellationToken);
        return player?.ToDto();
    }
}
