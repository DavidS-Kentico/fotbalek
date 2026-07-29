using Fotbalek.SharedKernel;

namespace Fotbalek.Domain.Entities;

/// <summary>
/// A sparse OVERRIDE: a row exists only where the user changed something, and its absence means
/// the default (everything on). No seeding on join, nothing to keep in sync, and an empty table is
/// a valid initial state (AI/notifications.md §3.3, §8.2).
/// <para>
/// Shape and cascade choices mirror <see cref="ChatReadState"/>, the closest existing analogue:
/// per-user, per-team, one row, cascading from both roots.
/// </para>
/// </summary>
public class NotificationPreference
{
    public int Id { get; set; }

    public int UserId { get; set; }

    /// <summary>Null is reserved for a future global-defaults tier; v1 never writes null
    /// (AI/notifications.md §8.2, §16 — deferred by decision).</summary>
    public int? TeamId { get; set; }

    public NotificationCategory Category { get; set; }

    /// <summary>v1 reads and writes the <see cref="NotificationChannel.InApp"/> bit only;
    /// <see cref="NotificationChannel.Push"/> is what phase 2 turns on without a migration.</summary>
    public NotificationChannel Channels { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public AppUser User { get; set; } = null!;
    public Team? Team { get; set; }
}
