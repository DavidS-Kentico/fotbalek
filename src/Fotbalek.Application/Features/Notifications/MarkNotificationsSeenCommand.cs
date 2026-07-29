using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Notifications;

/// <summary>
/// Stamps SeenAt on EVERY unseen row of the current user — not just the page that rendered. The
/// badge means "arrived since you last looked", and touching the bell IS looking; a badge that
/// stayed lit because unseen rows sat below the fold would reintroduce exactly the problem the
/// seen/read split solves (AI/notifications.md §7.2).
/// <para>
/// Idempotent by construction: a repeat call matches zero rows, which is what lets the bell run it
/// on every trigger click without caring whether that click opened or closed the panel.
/// </para>
/// </summary>
public sealed record MarkNotificationsSeenCommand : ICommand;

internal sealed class MarkNotificationsSeenCommandHandler(
    IAppDbContext db, IUserContext userContext, IEventCollector events)
    : ICommandHandler<MarkNotificationsSeenCommand>
{
    public async Task<Result> Handle(MarkNotificationsSeenCommand command, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not int userId)
            return Result.Failure(CommonErrors.NotAuthenticated);

        var now = DateTimeOffset.UtcNow;
        var stamped = await db.Notifications
            .Where(n => n.UserId == userId && n.SeenAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.SeenAt, now), cancellationToken);

        if (stamped > 0)
            events.Enqueue(new NotificationReadStateChangedEvent(userId));

        return Result.Success();
    }
}
