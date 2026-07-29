using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Application.Features.Notifications;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Players;

/// <summary>Deactivates a player. Captain only; never their own player; recent-activity rule applies.</summary>
public sealed record DeactivatePlayerCommand(int TeamId, int PlayerId) : ICommand;

internal sealed class DeactivatePlayerCommandHandler(
    IAppDbContext db, IUserContext userContext, TeamAccess teamAccess, IEventCollector events)
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

        // All four ladders filter on IsActive in both scopes, so this can change a #1 with no match
        // involved — and the snapshot would otherwise stay stale until the next match blamed an
        // innocent match for it. Refreshed SILENTLY: "you are now #1 because somebody left" is not a
        // thing to celebrate (AI/notifications.md §6.3).
        events.Enqueue(new LadderRefreshDueEvent(command.TeamId));
        return Result.Success();
    }
}
