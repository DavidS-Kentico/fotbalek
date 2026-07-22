using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Contracts.Players;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Players;

/// <summary>The CURRENT USER's claimed player in a team (null when none).</summary>
public sealed record GetMyPlayerQuery(int TeamId) : IQuery<PlayerDto?>;

internal sealed class GetMyPlayerQueryHandler(IAppDbContext db, IUserContext userContext, TeamAccess teamAccess)
    : IQueryHandler<GetMyPlayerQuery, PlayerDto?>
{
    public async Task<Result<PlayerDto?>> Handle(GetMyPlayerQuery query, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not int userId)
            return Result.Success<PlayerDto?>(null);
        if (!await teamAccess.IsMemberAsync(query.TeamId, cancellationToken))
            return Result.Failure<PlayerDto?>(CommonErrors.NotMember);
        var player = await db.Players.AsNoTracking()
            .FirstOrDefaultAsync(p => p.TeamId == query.TeamId && p.UserId == userId, cancellationToken);
        return player?.ToDto();
    }
}
