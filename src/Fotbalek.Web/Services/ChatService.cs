using Fotbalek.Web.Data;
using Fotbalek.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Web.Services;

/// <summary>
/// All chat DB operations. Every op takes an explicit teamId and independently re-verifies
/// membership — one dock acts on any of the user's teams, so there is no ambient "current
/// team" and nothing is inferred from the page the user happens to be on. Writes raise
/// <see cref="ChatNotifier"/> events after committing.
/// </summary>
public class ChatService(IDbContextFactory<AppDbContext> dbFactory, ChatNotifier notifier)
{
    public enum SendResult { Sent, NotMember, Empty, Throttled }

    /// <summary>Persists a message and notifies subscribers. The sender's own panel appends
    /// via the MessagePosted event too (the round trip is in-process). Sending inherently
    /// advances the sender's read watermark — the send context is open + selected + focused.</summary>
    public async Task<SendResult> SendAsync(int userId, int teamId, string body)
    {
        body = (body ?? string.Empty).Trim();
        if (body.Length == 0)
            return SendResult.Empty;
        if (body.Length > Constants.Chat.MaxMessageLength)
            body = body[..Constants.Chat.MaxMessageLength];

        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await IsMemberAsync(db, userId, teamId))
            return SendResult.NotMember;

        if (!notifier.TryRecordSend(userId))
            return SendResult.Throttled;

        var message = new ChatMessage
        {
            TeamId = teamId,
            SenderUserId = userId,
            Body = body,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ChatMessages.Add(message);
        await db.SaveChangesAsync();

        // Typing obviously ended with the send.
        notifier.SetTyping(teamId, userId, false);
        await AdvanceReadStateAsync(db, userId, teamId, message.Id);

        var sender = await ResolveSenderAsync(db, teamId, userId);
        var dto = new ChatMessageDto(
            message.Id, userId, sender.Name, sender.AvatarId,
            body, message.CreatedAt, IsDeleted: false, EditedAt: null, Reactions: []);
        notifier.NotifyMessagePosted(teamId, dto);
        return SendResult.Sent;
    }

    /// <summary>Author-only edit: replaces the body in place (same trim/clamp as send) and
    /// stamps EditedAt. Tombstones can't be edited; an unchanged body is a silent no-op.
    /// No banner and no unread impact — only the MessageEdited event fires.</summary>
    public async Task<bool> EditAsync(int userId, int teamId, int messageId, string newBody)
    {
        newBody = (newBody ?? string.Empty).Trim();
        if (newBody.Length == 0)
            return false;
        if (newBody.Length > Constants.Chat.MaxMessageLength)
            newBody = newBody[..Constants.Chat.MaxMessageLength];

        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await IsMemberAsync(db, userId, teamId))
            return false;

        var message = await db.ChatMessages
            .FirstOrDefaultAsync(m => m.Id == messageId && m.TeamId == teamId);
        if (message == null || message.SenderUserId != userId || message.IsDeleted)
            return false;
        if (message.Body == newBody)
            return true;

        message.Body = newBody;
        message.EditedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        notifier.NotifyMessageEdited(teamId, messageId, newBody, message.EditedAt.Value);
        return true;
    }

    /// <summary>Author-only soft delete: tombstones the message, empties the body and drops
    /// its reactions.</summary>
    public async Task<bool> DeleteAsync(int userId, int teamId, int messageId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await IsMemberAsync(db, userId, teamId))
            return false;

        var message = await db.ChatMessages
            .FirstOrDefaultAsync(m => m.Id == messageId && m.TeamId == teamId);
        if (message == null || message.SenderUserId != userId || message.IsDeleted)
            return false;

        message.IsDeleted = true;
        message.Body = string.Empty;
        await db.ChatMessageReactions.Where(r => r.MessageId == messageId).ExecuteDeleteAsync();
        await db.SaveChangesAsync();

        notifier.NotifyMessageDeleted(teamId, messageId);
        return true;
    }

    /// <summary>Toggles the user's reaction (unique per user+emoji+message → idempotent) and
    /// broadcasts the updated summary. Reacting to a tombstone is rejected.</summary>
    public async Task<bool> ToggleReactionAsync(int userId, int teamId, int messageId, string emoji)
    {
        emoji = (emoji ?? string.Empty).Trim();
        if (emoji.Length == 0 || emoji.Length > Constants.Chat.MaxReactionEmojiLength)
            return false;

        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await IsMemberAsync(db, userId, teamId))
            return false;

        var message = await db.ChatMessages.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == messageId && m.TeamId == teamId);
        if (message == null || message.IsDeleted)
            return false;

        var existing = await db.ChatMessageReactions
            .FirstOrDefaultAsync(r => r.MessageId == messageId && r.UserId == userId && r.Emoji == emoji);
        if (existing != null)
            db.ChatMessageReactions.Remove(existing);
        else
            db.ChatMessageReactions.Add(new ChatMessageReaction
            {
                MessageId = messageId,
                UserId = userId,
                Emoji = emoji,
                CreatedAt = DateTimeOffset.UtcNow,
            });

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Unique-index race (two tabs toggling at once) — the other toggle won;
            // fall through and broadcast the current state.
        }

        var summary = await LoadReactionSummaryAsync(db, messageId);
        notifier.NotifyReactionChanged(teamId, messageId, summary);
        return true;
    }

    /// <summary>
    /// The member's history floor as a message id: the newest message older than their
    /// JoinedAt (0 if none), so all subsequent queries are pure id comparisons. Computed once
    /// per panel open — a single seek on the (TeamId, CreatedAt) index. Returns null for
    /// non-members.
    /// </summary>
    public async Task<int?> GetJoinFloorIdAsync(int userId, int teamId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var membership = await db.TeamMemberships.AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == userId && m.TeamId == teamId);
        if (membership == null)
            return null;

        var floor = await db.ChatMessages
            .Where(m => m.TeamId == teamId && m.CreatedAt < membership.JoinedAt)
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => (int?)m.Id)
            .FirstOrDefaultAsync();
        return floor ?? 0;
    }

    /// <summary>
    /// One page of history, newest-first internally but returned oldest-first for display.
    /// Pass beforeId when scrolling back; the join floor caps how far back a member can read.
    /// Returns null for non-members.
    /// </summary>
    public async Task<List<ChatMessageDto>?> GetPageAsync(int userId, int teamId, int joinFloorId, int? beforeId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await IsMemberAsync(db, userId, teamId))
            return null;

        var query = db.ChatMessages.AsNoTracking()
            .Where(m => m.TeamId == teamId && m.Id > joinFloorId);
        if (beforeId is int b)
            query = query.Where(m => m.Id < b);

        var rows = await query
            .OrderByDescending(m => m.Id)
            .Take(Constants.Chat.HistoryPageSize)
            .Select(m => new
            {
                m.Id,
                m.SenderUserId,
                m.Body,
                m.CreatedAt,
                m.IsDeleted,
                m.EditedAt,
                SenderPlayer = db.Players
                    .Where(p => p.TeamId == teamId && p.UserId == m.SenderUserId)
                    .Select(p => new { p.Name, p.AvatarId })
                    .FirstOrDefault(),
                SenderUserName = m.Sender.UserName,
                Reactions = m.Reactions
                    .OrderBy(r => r.Id)
                    .Select(r => new { r.Id, r.Emoji, r.UserId })
                    .ToList(),
            })
            .ToListAsync();
        rows.Reverse();

        return rows.Select(r => new ChatMessageDto(
                r.Id,
                r.SenderUserId,
                r.SenderPlayer?.Name ?? r.SenderUserName ?? "Unknown",
                r.SenderPlayer?.AvatarId ?? 1,
                r.IsDeleted ? string.Empty : r.Body,
                r.CreatedAt,
                r.IsDeleted,
                r.EditedAt,
                r.Reactions
                    .GroupBy(x => x.Emoji)
                    .OrderBy(g => g.Min(x => x.Id))
                    .Select(g => new ChatReactionDto(g.Key, g.Select(x => x.UserId).ToList()))
                    .ToList()))
            .ToList();
    }

    /// <summary>Advances the user's watermark for a team (monotonic — never rewinds, so
    /// multiple tabs and out-of-order events are safe) and raises ReadStateChanged.</summary>
    public async Task MarkReadAsync(int userId, int teamId, int messageId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await IsMemberAsync(db, userId, teamId))
            return;
        await AdvanceReadStateAsync(db, userId, teamId, messageId);
    }

    /// <summary>
    /// Every member's read watermark for a team (userId → highest read <see cref="ChatMessage.Id"/>),
    /// powering the "seen by" readout on the caller's own messages. Members who have never opened
    /// the chat have no row and are simply absent (effective watermark 0). Empty for non-members.
    /// </summary>
    public async Task<Dictionary<int, int>> GetReadWatermarksAsync(int userId, int teamId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await IsMemberAsync(db, userId, teamId))
            return [];
        return await db.ChatReadStates.AsNoTracking()
            .Where(r => r.TeamId == teamId)
            .ToDictionaryAsync(r => r.UserId, r => r.LastReadMessageId);
    }

    /// <summary>
    /// Per-team unread counts for all teams where the user has claimed a Player, in one query:
    /// messages after both the join floor (CreatedAt ≥ JoinedAt) and the read watermark
    /// (Id &gt; LastReadMessageId, 0 when no row yet), excluding tombstones.
    /// </summary>
    public async Task<Dictionary<int, int>> GetUnreadCountsAsync(int userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.TeamMemberships.AsNoTracking()
            .Where(m => m.UserId == userId && m.Team.Players.Any(p => p.UserId == userId))
            .Select(m => new
            {
                m.TeamId,
                Count = db.ChatMessages.Count(c =>
                    c.TeamId == m.TeamId
                    && !c.IsDeleted
                    && c.CreatedAt >= m.JoinedAt
                    && c.Id > (db.ChatReadStates
                        .Where(r => r.UserId == userId && r.TeamId == m.TeamId)
                        .Select(r => (int?)r.LastReadMessageId)
                        .FirstOrDefault() ?? 0)),
            })
            .ToListAsync();
        return rows.ToDictionary(r => r.TeamId, r => r.Count);
    }

    /// <summary>
    /// The latest visible message in every team where the user has claimed a Player — powers
    /// the dock rail's last-message preview. One seek per team on the (TeamId, Id) index; the
    /// user's team count is small, matching the dock's existing per-team roster loads. Teams
    /// with no messages above the join floor are simply absent from the result.
    /// </summary>
    public async Task<Dictionary<int, ChatThreadPreview>> GetThreadPreviewsAsync(int userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var memberships = await db.TeamMemberships.AsNoTracking()
            .Where(m => m.UserId == userId && m.Team.Players.Any(p => p.UserId == userId))
            .Select(m => new { m.TeamId, m.JoinedAt })
            .ToListAsync();

        var result = new Dictionary<int, ChatThreadPreview>();
        foreach (var membership in memberships)
        {
            var preview = await LoadThreadPreviewAsync(db, membership.TeamId, membership.JoinedAt);
            if (preview != null)
                result[membership.TeamId] = preview;
        }
        return result;
    }

    /// <summary>Refreshes one team's rail preview — used when the shown last message is deleted
    /// and the previous one must surface. Returns null for non-members or an empty thread.</summary>
    public async Task<ChatThreadPreview?> GetThreadPreviewAsync(int userId, int teamId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var membership = await db.TeamMemberships.AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == userId && m.TeamId == teamId);
        return membership == null ? null : await LoadThreadPreviewAsync(db, teamId, membership.JoinedAt);
    }

    private static async Task<ChatThreadPreview?> LoadThreadPreviewAsync(AppDbContext db, int teamId, DateTimeOffset joinedAt)
    {
        var row = await db.ChatMessages.AsNoTracking()
            .Where(m => m.TeamId == teamId && !m.IsDeleted && m.CreatedAt >= joinedAt)
            .OrderByDescending(m => m.Id)
            .Select(m => new
            {
                m.Id,
                m.SenderUserId,
                m.Body,
                m.CreatedAt,
                SenderName = db.Players
                    .Where(p => p.TeamId == teamId && p.UserId == m.SenderUserId)
                    .Select(p => p.Name)
                    .FirstOrDefault(),
            })
            .FirstOrDefaultAsync();
        return row == null
            ? null
            : new ChatThreadPreview(row.Id, row.SenderUserId, row.SenderName ?? "Unknown", row.Body, row.CreatedAt);
    }

    /// <summary>Typing is ephemeral (lives in <see cref="ChatNotifier"/>), but authorization
    /// is never assumed from the circuit — membership is still verified.</summary>
    public async Task SetTypingAsync(int userId, int teamId, bool isTyping)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await IsMemberAsync(db, userId, teamId))
            return;
        notifier.SetTyping(teamId, userId, isTyping);
    }

    private static Task<bool> IsMemberAsync(AppDbContext db, int userId, int teamId) =>
        db.TeamMemberships.AnyAsync(m => m.UserId == userId && m.TeamId == teamId);

    private async Task<(string Name, int AvatarId)> ResolveSenderAsync(AppDbContext db, int teamId, int userId)
    {
        var player = await db.Players.AsNoTracking()
            .Where(p => p.TeamId == teamId && p.UserId == userId)
            .Select(p => new { p.Name, p.AvatarId })
            .FirstOrDefaultAsync();
        if (player != null)
            return (player.Name, player.AvatarId);

        // Purely defensive — every dock participant has a claimed Player (§1).
        var userName = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.UserName)
            .FirstOrDefaultAsync();
        return (userName ?? "Unknown", 1);
    }

    /// <summary>Monotonic watermark advance: a conditioned UPDATE so a concurrent tab's higher
    /// watermark can never be overwritten by a lower one, with insert-on-first-read.</summary>
    private async Task AdvanceReadStateAsync(AppDbContext db, int userId, int teamId, int messageId)
    {
        var advanced = await db.ChatReadStates
            .Where(r => r.UserId == userId && r.TeamId == teamId && r.LastReadMessageId < messageId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.LastReadMessageId, messageId));

        if (advanced == 0)
        {
            if (await db.ChatReadStates.AnyAsync(r => r.UserId == userId && r.TeamId == teamId))
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
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Unique-index race: another tab inserted first; retry as a guarded update.
                db.Entry(state).State = EntityState.Detached;
                await db.ChatReadStates
                    .Where(r => r.UserId == userId && r.TeamId == teamId && r.LastReadMessageId < messageId)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.LastReadMessageId, messageId));
            }
        }

        notifier.NotifyReadStateChanged(teamId, userId, messageId);
    }

    private static async Task<List<ChatReactionDto>> LoadReactionSummaryAsync(AppDbContext db, int messageId)
    {
        var rows = await db.ChatMessageReactions.AsNoTracking()
            .Where(r => r.MessageId == messageId)
            .OrderBy(r => r.Id)
            .Select(r => new { r.Id, r.Emoji, r.UserId })
            .ToListAsync();
        return rows
            .GroupBy(r => r.Emoji)
            .OrderBy(g => g.Min(x => x.Id))
            .Select(g => new ChatReactionDto(g.Key, g.Select(x => x.UserId).ToList()))
            .ToList();
    }
}
