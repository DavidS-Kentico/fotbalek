using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Players;

/// <summary>Deactivates a player. Captain only; never their own player; recent-activity rule applies.</summary>
public sealed record DeactivatePlayerCommand(int TeamId, int PlayerId) : ICommand;

internal sealed class DeactivatePlayerCommandHandler(IAppDbContext db, IUserContext userContext, TeamAccess teamAccess)
    : ICommandHandler<DeactivatePlayerCommand>
{
    public async Task<Result> Handle(DeactivatePlayerCommand command, CancellationToken cancellationToken)
    {
        var player = await db.Players.FindAsync([command.PlayerId], cancellationToken);
        if (player is null || player.TeamId != command.TeamId)
            return Result.Failure(Error.NotFound("Players.NotFound", "Player not found."));

        if (!await teamAccess.IsCaptainAsync(command.TeamId, cancellationToken))
            return Result.Failure(CommonErrors.NotCaptain);

        if (player.UserId == userContext.UserId)
            return Result.Failure(Error.Forbidden(
                "Players.CannotDeactivateSelf", "You cannot deactivate your own player."));

        var recentActivityThreshold = DateTimeOffset.UtcNow.AddDays(-Constants.TimeThresholds.RecentActivityDays);
        var hasRecentMatches = await db.MatchPlayers.AnyAsync(
            mp => mp.PlayerId == command.PlayerId && mp.Match.PlayedAt >= recentActivityThreshold,
            cancellationToken);
        if (hasRecentMatches)
            return Result.Failure(Error.Conflict(
                "Players.RecentActivity",
                $"Cannot deactivate player with matches in the last {Constants.TimeThresholds.RecentActivityDays} days."));

        player.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
