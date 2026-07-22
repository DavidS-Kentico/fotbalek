using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Application.Features.Teams;
using Fotbalek.Contracts.Players;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Players;

public sealed record GetPlayerWithTeamQuery(int PlayerId) : IQuery<PlayerWithTeamDto?>;

internal sealed class GetPlayerWithTeamQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<GetPlayerWithTeamQuery, PlayerWithTeamDto?>
{
    public async Task<Result<PlayerWithTeamDto?>> Handle(GetPlayerWithTeamQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberOfPlayerTeamAsync(query.PlayerId, cancellationToken))
            return Result.Failure<PlayerWithTeamDto?>(CommonErrors.NotMember);

        var player = await db.Players.AsNoTracking()
            .Include(p => p.Team)
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.Id == query.PlayerId, cancellationToken);
        return player is null
            ? null
            : new PlayerWithTeamDto(player.ToDto(), player.Team.ToDto(), player.User?.UserName);
    }
}
