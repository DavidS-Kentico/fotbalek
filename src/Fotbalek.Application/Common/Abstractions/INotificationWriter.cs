using Fotbalek.SharedKernel;

namespace Fotbalek.Application.Common.Abstractions;

/// <summary>
/// One notification to fan out, minus the recipients. Everything is an id, an enum or a number —
/// no wording, no icon and no URL, which all live in Web (AI/notifications.md §3.2, §10).
/// </summary>
/// <param name="Type">What happened.</param>
/// <param name="TeamId">Where it happened — the label, the navigation target and the preference axis.</param>
/// <param name="DedupKey">Server-composed idempotency key, never built from user input (§4.3).</param>
public sealed record NotificationDraft(NotificationType Type, int TeamId, string DedupKey)
{
    /// <summary>
    /// The acting USER, excluded from the recipients. Deliberately separate from
    /// <see cref="ActorPlayerId"/>, which is display-only: the ladder-lead and milestone drafts set
    /// NEITHER, because they are system rows and the recorder must still receive their own
    /// (self-recorded matches are the common case — AI/notifications.md §4.2).
    /// </summary>
    public int? ActorUserId { get; init; }

    /// <summary>Display-only: who caused it, as their claimed player in this team. Null ⇒ a system
    /// row, which is what makes the UI draw a per-type icon instead of a face (§9.1).</summary>
    public int? ActorPlayerId { get; init; }

    /// <summary>The other player the row is about: a duo partner, or whoever took the lead.</summary>
    public int? SubjectPlayerId { get; init; }

    public int? MatchId { get; init; }
    public int? SeasonId { get; init; }
    public int? ChatMessageId { get; init; }

    /// <summary>Award / ladder category — one of Constants.Seasons.AwardCategories.</summary>
    public string? Category { get; init; }

    /// <summary>The one number the type needs (rank, streak length, match count, peak ELO).</summary>
    public int? Value { get; init; }

    /// <summary>Reaction rows only.</summary>
    public string? Emoji { get; init; }
}

/// <summary>
/// Writes notification rows inside the acting command's transaction. A notification for an action
/// that rolled back would be a lie, and the caller has the actor, the ids and the recipients right
/// there; only DELIVERY is post-commit, through the event collector (AI/notifications.md §4.1).
/// </summary>
public interface INotificationWriter
{
    /// <summary>
    /// Queues one row per recipient that still wants this category in this team. Drops the draft's
    /// own actor and anything the dedup key already covers. Async because it resolves preferences (§8.3).
    /// <para>
    /// <b>ADDS TO THE CHANGE TRACKER ONLY — the caller must SaveChanges.</b> This call also enqueues
    /// one delivery event per surviving row, and the collector is flushed after commit regardless of
    /// what the handler did in between; a handler that calls this and never reaches a
    /// <c>SaveChangesAsync</c> would therefore publish events for rows that were never inserted.
    /// Bulk <c>ExecuteUpdate</c>/<c>ExecuteDelete</c> statements do not count — they bypass the
    /// change tracker entirely (§4.1).
    /// </para>
    /// </summary>
    Task AddAsync(NotificationDraft draft, IEnumerable<int> recipientUserIds, CancellationToken cancellationToken);
}
