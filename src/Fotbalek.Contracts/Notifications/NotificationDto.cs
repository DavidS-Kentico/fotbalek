using Fotbalek.SharedKernel;

namespace Fotbalek.Contracts.Notifications;

/// <summary>
/// One feed row. Carries the ids, the numbers and the display names the row needs — the wording,
/// the icon and the navigation target are composed in Web from <see cref="Type"/>
/// (AI/notifications.md §10). The chat message BODY is deliberately not here: the row links to the
/// message instead of copying it (§11).
/// </summary>
public record NotificationDto(
    int Id,
    int TeamId,
    string TeamName,
    // Needed to build the row's target URL, which is always team-scoped.
    string TeamCodeName,
    // The RECIPIENT's own claimed player in that team, resolved at read time. It is what the personal
    // milestones target — those rows are about the reader, so neither the actor (they have none) nor
    // the subject names the right player.
    int? RecipientPlayerId,
    NotificationType Type,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SeenAt,
    DateTimeOffset? ReadAt,
    // Null ⇔ a system row (season lifecycle, ladder leads, milestones), which is what the UI's
    // avatar-versus-icon choice keys on (§9.1).
    int? ActorPlayerId,
    string? ActorName,
    int? ActorAvatarId,
    int? SubjectPlayerId,
    string? SubjectName,
    int? MatchId,
    int? SeasonId,
    string? SeasonName,
    int? ChatMessageId,
    // Award / ladder category (Player / Goalkeeper / Attacker / Pair).
    string? Category,
    int? Value,
    string? Emoji)
{
    /// <summary>Drives the row's unread dot and heavier weight (§7.1).</summary>
    public bool IsRead => ReadAt != null;
}
