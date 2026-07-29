using Fotbalek.Application.Features.Notifications;
using MediatR;

namespace Fotbalek.Web.Realtime;

// The realtime bridge (AI/architecture.md §4.4): the notification writer and the read-state commands
// enqueue INotifications which the TransactionBehavior publishes after commit; these handlers forward
// them to NotificationNotifier, which fans out to circuits. Registered because AddApplication() scans
// the Web assembly too (§4.2).
//
// Only the created / read-state pair lives here. The match-aftermath and ladder-refresh bridges are
// Application's, beside the commands they dispatch — nothing about those dispatches touches a Web
// concern (AI/notifications.md §6.3).

internal sealed class NotificationCreatedBridge(NotificationNotifier notifier)
    : INotificationHandler<NotificationCreatedEvent>
{
    public Task Handle(NotificationCreatedEvent notification, CancellationToken cancellationToken)
    {
        notifier.NotifyCreated(notification.UserId);
        return Task.CompletedTask;
    }
}

internal sealed class NotificationReadStateChangedBridge(NotificationNotifier notifier)
    : INotificationHandler<NotificationReadStateChangedEvent>
{
    public Task Handle(NotificationReadStateChangedEvent notification, CancellationToken cancellationToken)
    {
        notifier.NotifyReadStateChanged(notification.UserId);
        return Task.CompletedTask;
    }
}
