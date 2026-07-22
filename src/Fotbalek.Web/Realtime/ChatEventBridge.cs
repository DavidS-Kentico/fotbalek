using Fotbalek.Application.Features.Chat;
using MediatR;

namespace Fotbalek.Web.Realtime;

// The realtime bridge (§4.4): Application chat handlers enqueue INotifications which the
// TransactionBehavior publishes after commit; these handlers forward them to ChatNotifier,
// which keeps fanning out to circuits exactly as before. Registered because AddApplication()
// scans the Web assembly too (§4.2).

internal sealed class ChatMessagePostedBridge(ChatNotifier notifier) : INotificationHandler<ChatMessagePostedEvent>
{
    public Task Handle(ChatMessagePostedEvent notification, CancellationToken cancellationToken)
    {
        // Typing obviously ended with the send — cleared here (host-side ephemeral state).
        notifier.SetTyping(notification.TeamId, notification.Message.SenderUserId, false);
        notifier.NotifyMessagePosted(notification.TeamId, notification.Message);
        return Task.CompletedTask;
    }
}

internal sealed class ChatMessageEditedBridge(ChatNotifier notifier) : INotificationHandler<ChatMessageEditedEvent>
{
    public Task Handle(ChatMessageEditedEvent notification, CancellationToken cancellationToken)
    {
        notifier.NotifyMessageEdited(notification.TeamId, notification.MessageId, notification.Body, notification.EditedAt);
        return Task.CompletedTask;
    }
}

internal sealed class ChatMessageDeletedBridge(ChatNotifier notifier) : INotificationHandler<ChatMessageDeletedEvent>
{
    public Task Handle(ChatMessageDeletedEvent notification, CancellationToken cancellationToken)
    {
        notifier.NotifyMessageDeleted(notification.TeamId, notification.MessageId);
        return Task.CompletedTask;
    }
}

internal sealed class ChatReactionChangedBridge(ChatNotifier notifier) : INotificationHandler<ChatReactionChangedEvent>
{
    public Task Handle(ChatReactionChangedEvent notification, CancellationToken cancellationToken)
    {
        notifier.NotifyReactionChanged(notification.TeamId, notification.MessageId, notification.Reactions);
        return Task.CompletedTask;
    }
}

internal sealed class ChatReadStateChangedBridge(ChatNotifier notifier) : INotificationHandler<ChatReadStateChangedEvent>
{
    public Task Handle(ChatReadStateChangedEvent notification, CancellationToken cancellationToken)
    {
        notifier.NotifyReadStateChanged(notification.TeamId, notification.UserId, notification.LastReadMessageId);
        return Task.CompletedTask;
    }
}
