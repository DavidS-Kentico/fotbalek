using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Notifications;

/// <summary>
/// Marks one row read — what clicking it does, before navigating or opening the chat dock.
/// Owner-scoped: the filter is the current user's id, so marking someone else's row read is not
/// expressible (AI/notifications.md §11).
/// </summary>
public sealed record MarkNotificationReadCommand(int NotificationId) : ICommand;

internal sealed class MarkNotificationReadCommandHandler(
    IAppDbContext db, IUserContext userContext, IEventCollector events)
    : ICommandHandler<MarkNotificationReadCommand>
{
    public async Task<Result> Handle(MarkNotificationReadCommand command, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not int userId)
            return Result.Failure(CommonErrors.NotAuthenticated);

        var now = DateTimeOffset.UtcNow;
        var updated = await db.Notifications
            .Where(n => n.Id == command.NotificationId && n.UserId == userId && n.ReadAt == null)
            // ReadAt implies SeenAt, so the two can never contradict — a row you read but never
            // "saw" is nonsense, and would leave the badge counting it (§7.2).
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.ReadAt, now)
                .SetProperty(n => n.SeenAt, n => n.SeenAt ?? now),
                cancellationToken);

        if (updated > 0)
            events.Enqueue(new NotificationReadStateChangedEvent(userId));

        return Result.Success();
    }
}
