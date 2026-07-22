using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Chat;

/// <summary>
/// Monotonic watermark advance shared by send and mark-read: a conditioned UPDATE so a
/// concurrent tab's higher watermark can never be overwritten by a lower one, with
/// insert-on-first-read. Enqueues ChatReadStateChangedEvent when it (possibly) moved.
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

        events.Enqueue(new ChatReadStateChangedEvent(teamId, userId, messageId));
    }
}
