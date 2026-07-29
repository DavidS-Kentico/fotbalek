using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Features.Notifications;
using Fotbalek.Domain.Entities;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Chat;

/// <summary>
/// Monotonic watermark advance shared by send and mark-read: a conditioned UPDATE so a
/// concurrent tab's higher watermark can never be overwritten by a lower one, with
/// insert-on-first-read. Enqueues ChatReadStateChangedEvent when it (possibly) moved.
/// <para>
/// It also clears the bell's chat rows up to the new watermark — hooked HERE rather than on
/// MarkChatReadCommand precisely because SendChatMessageCommand calls this too: sending inherently
/// marks the conversation read, and hooking the command would leave the bell row bold after you
/// answered a mention in chat, which is the exact contradiction the rule exists to prevent
/// (AI/notifications.md §7.3).
/// </para>
/// </summary>
internal static class ChatReadStateAdvancer
{
    public static async Task AdvanceAsync(
        IAppDbContext db, IEventCollector events, int userId, int teamId, int messageId,
        CancellationToken cancellationToken)
    {
        var advanced = await db.ChatReadStates
            .Where(r => r.UserId == userId && r.TeamId == teamId && r.LastReadMessageId < messageId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.LastReadMessageId, messageId), cancellationToken);

        if (advanced == 0)
        {
            if (await db.ChatReadStates.AnyAsync(r => r.UserId == userId && r.TeamId == teamId, cancellationToken))
                return; // already at/above messageId — nothing to broadcast

            var state = new ChatReadState
            {
                UserId = userId,
                TeamId = teamId,
                LastReadMessageId = messageId,
            };
            db.ChatReadStates.Add(state);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Unique-index race: another tab inserted first; retry as a guarded update.
                db.Entry(state).State = EntityState.Detached;
                await db.ChatReadStates
                    .Where(r => r.UserId == userId && r.TeamId == teamId && r.LastReadMessageId < messageId)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.LastReadMessageId, messageId), cancellationToken);
            }
        }

        await ClearBellChatRowsAsync(db, events, userId, teamId, messageId, cancellationToken);
        events.Enqueue(new ChatReadStateChangedEvent(teamId, userId, messageId));
    }

    /// <summary>
    /// A mention or reaction the user has already seen in the chat panel must not keep nagging in the
    /// bell — the one place the two features are coupled, and worth it, because without it the bell
    /// contradicts the panel.
    /// <para>
    /// Placed after the successful-advance paths rather than before the "already at or above" early
    /// return: the rows can only have been created below a watermark that has not yet moved past them.
    /// It also sits inside the member-gated path, so MarkChatReadCommand's silent non-member branch
    /// never reaches it.
    /// </para>
    /// </summary>
    private static async Task ClearBellChatRowsAsync(
        IAppDbContext db, IEventCollector events, int userId, int teamId, int messageId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var cleared = await db.Notifications
            .Where(n => n.UserId == userId && n.ReadAt == null && n.TeamId == teamId)
            .Where(n => n.Type == NotificationType.ChatMention || n.Type == NotificationType.ChatReaction)
            .Where(n => n.ChatMessageId != null && n.ChatMessageId <= messageId)
            // ReadAt implies SeenAt (§7.2).
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.ReadAt, now)
                .SetProperty(n => n.SeenAt, n => n.SeenAt ?? now),
                cancellationToken);

        if (cleared > 0)
            events.Enqueue(new NotificationReadStateChangedEvent(userId));
    }
}
