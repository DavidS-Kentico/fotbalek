namespace Fotbalek.SharedKernel;

/// <summary>
/// The preference grouping a <see cref="NotificationType"/> belongs to — five categories rather
/// than twelve types, because twelve toggles per team is a settings page nobody reads
/// (AI/notifications.md §8.1). The category LABELS are Web's job; this is a classification.
/// <para>
/// Not to be confused with the <c>Category</c> string on a notification row, which carries the
/// award/ladder category (Player / Goalkeeper / Attacker / Pair).
/// </para>
/// </summary>
public enum NotificationCategory
{
    /// <summary>"Someone entered a match with me."</summary>
    Matches = 1,

    /// <summary>"Someone mentioned me / reacted to me."</summary>
    Chat = 2,

    /// <summary>"Season lifecycle and my result."</summary>
    Seasons = 3,

    /// <summary>"The #1 spots changed."</summary>
    Rankings = 4,

    /// <summary>"Personal highlights."</summary>
    Milestones = 5,
}
