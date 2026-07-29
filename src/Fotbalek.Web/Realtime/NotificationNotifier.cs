namespace Fotbalek.Web.Realtime;

/// <summary>
/// In-process pub/sub for the notification bell, following <see cref="ChatNotifier"/>'s pattern: the
/// Application handlers do the DB write, their post-commit events are forwarded here by the
/// NotificationEventBridge, and every subscribed circuit re-renders.
/// <para>
/// The shared part with <see cref="ChatNotifier"/> is the filtering-subscriber idea, not the key:
/// chat's events key on a team, these key on a <b>user</b>, because a notification belongs to an
/// account rather than a team (AI/notifications.md §1, §4.4). Subscribers ignore other users' events.
/// </para>
/// <para>
/// Single server instance assumed, the same caveat as ChatNotifier, PresenceTracker and
/// GameRoomManager. Rows are persisted, so a restart loses nothing but live fan-out to circuits that
/// are already connected (§14).
/// </para>
/// </summary>
public sealed class NotificationNotifier(ILogger<NotificationNotifier> logger)
{
    /// <summary>(recipient userId) — a row arrived. Subscribers recompute rather than increment.</summary>
    public event Action<int>? Created;

    /// <summary>(userId) — their seen/read state moved, so their other tabs must catch up.</summary>
    public event Action<int>? ReadStateChanged;

    public void NotifyCreated(int userId) => Raise(() => Created?.Invoke(userId));

    public void NotifyReadStateChanged(int userId) => Raise(() => ReadStateChanged?.Invoke(userId));

    private void Raise(Action raise)
    {
        try
        {
            raise();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "NotificationNotifier subscriber threw");
        }
    }
}
