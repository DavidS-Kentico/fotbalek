using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Application.Features.Notifications;
using Fotbalek.SharedKernel;

namespace Fotbalek.Application.Features.Players;

/// <summary>Reactivates a player. Captain only.</summary>
public sealed record ReactivatePlayerCommand(int TeamId, int PlayerId) : ICommand;

internal sealed class ReactivatePlayerCommandHandler(
    IAppDbContext db, TeamAccess teamAccess, IEventCollector events)
    : ICommandHandler<ReactivatePlayerCommand>
{
    public async Task<Result> Handle(ReactivatePlayerCommand command, CancellationToken cancellationToken)
    {
        var player = await db.Players.FindAsync([command.PlayerId], cancellationToken);
        if (player is null || player.TeamId != command.TeamId)
            return Result.Failure(Error.NotFound("Players.NotFound", "Player not found."));

        if (!await teamAccess.IsCaptainAsync(command.TeamId, cancellationToken))
            return Result.Failure(CommonErrors.NotCaptain);

        player.IsActive = true;
        await db.SaveChangesAsync(cancellationToken);

        // Re-entering the ladders can change a #1 with no match involved — refresh the snapshot
        // silently, exactly as deactivation does (AI/notifications.md §6.3).
        events.Enqueue(new LadderRefreshDueEvent(command.TeamId));
        return Result.Success();
    }
}
