using Fotbalek.SharedKernel;

namespace Fotbalek.Domain.Entities;

/// <summary>
/// One row per RECIPIENT — the write fans out rather than sharing a row through a join table. At
/// this scale (a handful of recipients per event) the duplication is trivial and it makes both the
/// unseen count and the read flag a single-row concern (AI/notifications.md §3.1).
/// <para>
/// A notification belongs to an ACCOUNT, not to a team: the feed is owner-scoped and spans every
/// team the user is in. <see cref="TeamId"/> is a label, a navigation target and the axis
/// preferences filter on — it never partitions the feed (§1).
/// </para>
/// <para>
/// Cascade shape: <see cref="TeamId"/> cascades, so every other FK here must not — Team → Match →
/// Notification alongside Team → Notification would be two delete paths from one root, which SQL
/// Server rejects. The consequence is that the two hard-delete paths in the app
/// (DeleteMatchCommand, DeleteSeasonCommand) clean their rows up explicitly.
/// </para>
/// </summary>
public class Notification
{
    /// <summary>Identity — monotonic; the ordering key and the pagination cursor (same reasoning
    /// as <see cref="ChatMessage.Id"/>).</summary>
    public int Id { get; set; }

    /// <summary>The recipient.</summary>
    public int UserId { get; set; }

    /// <summary>Where it happened: a label, a navigation target and the preference axis.</summary>
    public int TeamId { get; set; }

    public NotificationType Type { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Null = never shown to the user. Drives the BADGE (§7.1).</summary>
    public DateTimeOffset? SeenAt { get; set; }

    /// <summary>Null = unread. Drives the row's own unread styling (§7.1). Setting this always
    /// sets <see cref="SeenAt"/> too — a row read but never seen would leave the badge counting it.</summary>
    public DateTimeOffset? ReadAt { get; set; }

    /// <summary>Who caused it, as their claimed player in this team; name and avatar are resolved
    /// at read time. Null for system events (season lifecycle, ladder leads, milestones), which is
    /// exactly the actor-row / system-row split the UI's icon choice keys on (§9.1).</summary>
    public int? ActorPlayerId { get; set; }

    /// <summary>The OTHER player the row is about: the duo partner for a pair lead or award, or
    /// the player who took the lead from you.</summary>
    public int? SubjectPlayerId { get; set; }

    public int? MatchId { get; set; }
    public int? SeasonId { get; set; }
    public int? ChatMessageId { get; set; }

    /// <summary>For ladder and award rows: one of Constants.Seasons.AwardCategories
    /// (Player / Goalkeeper / Attacker / Pair). Unrelated to NotificationCategory, which is the
    /// PREFERENCE grouping (§3.1, §8.1).</summary>
    public string? Category { get; set; }

    /// <summary>The one number the type needs: final rank, award rank, streak length, match count
    /// or new peak ELO.</summary>
    public int? Value { get; set; }

    /// <summary>Reaction rows only. Display-only, so unlike
    /// <see cref="ChatMessageReaction.Emoji"/> it needs no binary collation — nothing compares or
    /// uniquely indexes it.</summary>
    public string? Emoji { get; set; }

    /// <summary>Idempotency key, composed server-side from ids and enum values only (§4.3).
    /// Unique per (UserId, DedupKey).</summary>
    public string DedupKey { get; set; } = string.Empty;

    public AppUser User { get; set; } = null!;
    public Team Team { get; set; } = null!;
    public Player? ActorPlayer { get; set; }
    public Player? SubjectPlayer { get; set; }
    public Match? Match { get; set; }
    public Season? Season { get; set; }
    public ChatMessage? ChatMessage { get; set; }
}
