using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Notifications;

/// <summary>
/// The panel header's "Mark all as read" — how you clear a backlog of bold rows you have decided
/// not to open (AI/notifications.md §7.2). Account-wide, like the feed.
/// </summary>
public sealed record MarkAllNotificationsReadCommand : ICommand;

internal sealed class MarkAllNotificationsReadCommandHandler(
    IAppDbContext db, IUserContext userContext, IEventCollector events)
    : ICommandHandler<MarkAllNotificationsReadCommand>
{
    public async Task<Result> Handle(MarkAllNotificationsReadCommand command, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not int userId)
            return Result.Failure(CommonErrors.NotAuthenticated);

        var now = DateTimeOffset.UtcNow;
        var updated = await db.Notifications
            .Where(n => n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.ReadAt, now)
                .SetProperty(n => n.SeenAt, n => n.SeenAt ?? now),
                cancellationToken);

        if (updated > 0)
            events.Enqueue(new NotificationReadStateChangedEvent(userId));

        return Result.Success();
    }
}
