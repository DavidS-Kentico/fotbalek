namespace Fotbalek.Contracts.Chat;

/// <summary>
/// One chat message as rendered by the UI (server → component). Sender display name/avatar
/// are resolved from the sender's claimed Player in the message's team at load/post time;
/// deleted messages never carry a body over the wire.
/// </summary>
public record ChatMessageDto(
    int Id,
    int SenderUserId,
    string SenderName,
    int SenderAvatarId,
    string Body,
    DateTimeOffset CreatedAt,
    bool IsDeleted,
    DateTimeOffset? EditedAt,
    List<ChatReactionDto> Reactions);

/// <summary>One reaction chip: an emoji plus who reacted (count = UserIds.Count).
/// Subscribers highlight their own reactions by looking for their user id.</summary>
public record ChatReactionDto(string Emoji, List<int> UserIds);

/// <summary>
/// The latest non-deleted message in a team, for the dock rail's last-message preview.
/// MessageId lets the live cache decide whether an edit/delete affects the shown preview.
/// </summary>
public record ChatThreadPreview(
    int MessageId,
    int SenderUserId,
    string SenderName,
    string Body,
    DateTimeOffset CreatedAt);

/// <summary>One member in a "seen by" readout — enough to draw a tiny avatar and name it.</summary>
public record ChatSeenViewer(int UserId, string Name, int AvatarId);

/// <summary>
/// Who has (and hasn't) seen a given message, derived from read watermarks: a member has seen
/// it when their stored last-read message id ≥ the message id. The author is never in either
/// list. Shown only on the author's own latest message.
/// </summary>
public record ChatSeenState(
    IReadOnlyList<ChatSeenViewer> Seen,
    IReadOnlyList<ChatSeenViewer> NotSeen)
{
    /// <summary>Every other member has seen it (and there is at least one other member).</summary>
    public bool AllSeen => NotSeen.Count == 0 && Seen.Count > 0;
}
