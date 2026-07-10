namespace Fotbalek.Web.Data.Entities;

public class ChatMessageReaction
{
    public int Id { get; set; }
    public int MessageId { get; set; }
    public int UserId { get; set; }
    /// <summary>A single emoji (may be a multi-codepoint / ZWJ sequence, e.g. 👍🏽).
    /// Stored under a binary collation — see AppDbContext.</summary>
    public string Emoji { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ChatMessage Message { get; set; } = null!;
    public AppUser User { get; set; } = null!;
}
