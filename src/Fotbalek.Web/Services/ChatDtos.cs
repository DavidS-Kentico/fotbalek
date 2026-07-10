namespace Fotbalek.Web.Services;

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
