using Fotbalek.Application.Common.Abstractions;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Chat;

/// <summary>
/// Turns the mentions in a message body into bell rows. Shared by send and edit so the two cannot
/// disagree; on an edit the <c>mention:{messageId}</c> dedup key means only NEWLY added mentions
/// notify (AI/notifications.md §5.2).
/// <para>
/// Adds tracked rows only — <b>the caller must SaveChanges</b> (§4.1).
/// </para>
/// </summary>
internal static class ChatMentionWriter
{
    public static async Task WriteAsync(
        IAppDbContext db,
        INotificationWriter writer,
        int teamId,
        int senderUserId,
        int messageId,
        string body,
        CancellationToken cancellationToken)
    {
        // The roster INCLUDES inactive players, matching what the dock loads for its pills
        // (GetTeamPlayersQuery with IncludeInactive: true). Otherwise a mention of a deactivated
        // player would render as a pill and produce no row — and a deactivated player with a claimed
        // account is still a person on the team.
        var roster = await db.Players
            .AsNoTracking()
            .Where(p => p.TeamId == teamId)
            .Select(p => new { p.Id, p.Name, p.UserId })
            .ToListAsync(cancellationToken);
        if (roster.Count == 0)
            return;

        var spans = MentionScanner.Scan(body, roster.Select(p => new RosterName(p.Id, p.Name)).ToList());
        if (spans.Count == 0)
            return;

        var mentioned = spans.Select(s => s.PlayerId).ToHashSet();
        var recipients = roster
            .Where(p => mentioned.Contains(p.Id) && p.UserId != null)
            .Select(p => p.UserId!.Value)
            .Distinct()
            .ToList();
        if (recipients.Count == 0)
            return;

        await writer.AddAsync(
            new NotificationDraft(NotificationType.ChatMention, teamId, $"mention:{messageId}")
            {
                // Carrying the sender as the actor is what drops a self-mention.
                ActorUserId = senderUserId,
                ActorPlayerId = roster.FirstOrDefault(p => p.UserId == senderUserId)?.Id,
                ChatMessageId = messageId,
            },
            recipients,
            cancellationToken);
    }
}
