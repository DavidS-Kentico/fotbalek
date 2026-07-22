namespace Fotbalek.Domain.Entities;

public class ChatMessage
{
    /// <summary>Identity — monotonic, doubles as the chronological ordering key and the
    /// unread watermark (avoids clock-skew ordering bugs).</summary>
    public int Id { get; set; }
    public int TeamId { get; set; }
    /// <summary>Stable identity; display name/avatar are resolved live from the sender's
    /// claimed <see cref="Player"/> in this team.</summary>
    public int SenderUserId { get; set; }
    /// <summary>Raw user text — never rendered as MarkupString. Emptied on delete.</summary>
    public string Body { get; set; } = string.Empty;
    /// <summary>Used only for the since-joined floor and day grouping; ordering is by Id.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Soft delete — renders as a "message deleted" tombstone.</summary>
    public bool IsDeleted { get; set; }
    /// <summary>Set on each author edit (author-only, like delete); null = never edited.
    /// Renders as an "(edited)" marker.</summary>
    public DateTimeOffset? EditedAt { get; set; }

    public Team Team { get; set; } = null!;
    public AppUser Sender { get; set; } = null!;
    public ICollection<ChatMessageReaction> Reactions { get; set; } = new List<ChatMessageReaction>();
}
