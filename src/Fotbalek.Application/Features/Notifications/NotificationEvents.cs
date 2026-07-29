using MediatR;

namespace Fotbalek.Application.Features.Notifications;

// Delivery events for the realtime notification bridge (the documented MediatR exception,
// AI/architecture.md §4.2/§4.4): the writer and the read-state commands enqueue these on
// IEventCollector, the TransactionBehavior publishes them after a successful commit, and Web's
// bridge INotificationHandlers forward them to NotificationNotifier, which fans out to circuits.

/// <summary>
/// A row arrived for this user. Carries the recipient only: every surface recomputes from the DB
/// rather than incrementing a local counter, the same discipline chat's unread counts follow — a
/// count query cannot drift the way a counter can (AI/notifications.md §7.2).
/// </summary>
public sealed record NotificationCreatedEvent(int UserId) : INotification;

/// <summary>
/// This user's seen/read state moved, so their OTHER TABS must catch up — exactly what chat's
/// ReadStateChanged does. Subscribers ignore other users' events (§7.2).
/// </summary>
public sealed record NotificationReadStateChangedEvent(int UserId) : INotification;
