using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Players;

/// <summary>Claims an unclaimed placeholder player for the caller.</summary>
public sealed record ClaimPlayerCommand(int TeamId, int PlayerId) : ICommand;

internal sealed class ClaimPlayerCommandHandler(IAppDbContext db, IUserContext userContext, TeamAccess teamAccess)
    : ICommandHandler<ClaimPlayerCommand>
{
    public async Task<Result> Handle(ClaimPlayerCommand command, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not int userId)
            return Result.Failure(CommonErrors.NotAuthenticated);

        // Defense in depth: the caller must already be a member of the team.
        // The UI gates this, but the handler must not trust UI-only checks.
        if (!await teamAccess.IsMemberAsync(command.TeamId, cancellationToken))
            return Result.Failure(CommonErrors.NotMember);

        var claimError = Error.Conflict(
            "Players.ClaimFailed", "Could not claim that player. It may have just been taken; please refresh.");

        if (await PlayerRules.HasClaimedPlayerAsync(db, command.TeamId, userId, cancellationToken))
            return Result.Failure(claimError);

        var player = await db.Players.FindAsync([command.PlayerId], cancellationToken);
        if (player is null || player.TeamId != command.TeamId || player.UserId != null)
            return Result.Failure(claimError);

        player.UserId = userId;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Unique filtered index race — someone else claimed concurrently.
            return Result.Failure(claimError);
        }
        return Result.Success();
    }
}
