using Fotbalek.Contracts.Chat;
using MediatR;

namespace Fotbalek.Application.Features.Chat;

// Domain events for the realtime chat bridge (documented MediatR exception, §4.2/§4.4):
// handlers enqueue these on IEventCollector; the TransactionBehavior publishes them after a
// successful commit; Web's bridge INotificationHandlers forward them to ChatNotifier, which
// fans out to circuits.

public sealed record ChatMessagePostedEvent(int TeamId, ChatMessageDto Message) : INotification;

public sealed record ChatMessageEditedEvent(int TeamId, int MessageId, string Body, DateTimeOffset EditedAt) : INotification;

public sealed record ChatMessageDeletedEvent(int TeamId, int MessageId) : INotification;

public sealed record ChatReactionChangedEvent(int TeamId, int MessageId, List<ChatReactionDto> Reactions) : INotification;

public sealed record ChatReadStateChangedEvent(int TeamId, int UserId, int LastReadMessageId) : INotification;
