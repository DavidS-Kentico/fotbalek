namespace Fotbalek.Web.Data.Entities;

/// <summary>
/// Per-user, per-team unread watermark. Created/updated the first time the user reads the
/// team's chat; before it exists, the effective watermark is the join floor (messages since
/// <see cref="TeamMembership.JoinedAt"/>).
/// </summary>
public class ChatReadState
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int TeamId { get; set; }
    /// <summary>Highest <see cref="ChatMessage.Id"/> this user has read in this team.
    /// Only ever moves forward.</summary>
    public int LastReadMessageId { get; set; }

    public AppUser User { get; set; } = null!;
    public Team Team { get; set; } = null!;
}
