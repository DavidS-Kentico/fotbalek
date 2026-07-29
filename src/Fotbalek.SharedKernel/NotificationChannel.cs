namespace Fotbalek.SharedKernel;

/// <summary>
/// How a category may reach the user. A flags enum rather than a bool so phase 2's browser-push
/// toggle is a second bit on the same preference row — no second table and no migration
/// (AI/notifications.md §8.2, §13.2). v1 reads and writes <see cref="InApp"/> only.
/// </summary>
[Flags]
public enum NotificationChannel
{
    None = 0,

    /// <summary>The bell feed. Default-on for every category (AI/notifications.md §8.2).</summary>
    InApp = 1,

    /// <summary>Web Push — designed, not built (AI/notifications.md §13). Will default to off.</summary>
    Push = 2,
}
