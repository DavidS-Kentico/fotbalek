using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Domain.Entities;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Notifications;

/// <summary>
/// The one place notification rows are created. Scoped, so its two caches live exactly as long as
/// the dispatch — which is what makes the aftermath run (several drafts in one dispatch) one muted
/// lookup per category rather than one per draft (AI/notifications.md §8.3).
/// </summary>
internal sealed class NotificationWriter(IAppDbContext db, IEventCollector events) : INotificationWriter
{
    private readonly Dictionary<(int TeamId, NotificationCategory Category), HashSet<int>> _mutedCache = [];
    private readonly HashSet<(int UserId, string DedupKey)> _queuedThisScope = [];

    public async Task AddAsync(
        NotificationDraft draft, IEnumerable<int> recipientUserIds, CancellationToken cancellationToken)
    {
        var recipients = recipientUserIds.Distinct().ToList();
        if (draft.ActorUserId is int actorUserId)
            recipients.Remove(actorUserId);
        if (recipients.Count == 0)
            return;

        // Preferences are enforced HERE rather than at display time, so the unseen count can never
        // disagree with what the feed shows and no dead rows accumulate for a muted category. The
        // accepted cost is that re-enabling a category reveals nothing retroactively (§8.3).
        var muted = await ResolveMutedAsync(draft.TeamId, NotificationCategories.Of(draft.Type), cancellationToken);
        recipients.RemoveAll(muted.Contains);
        if (recipients.Count == 0)
            return;

        // Idempotency is filtered, not caught: the unique (UserId, DedupKey) index is the backstop,
        // but exceptions are not flow control (AI/architecture.md §4.1). Both halves matter — the
        // batch may repeat a key within one dispatch, and a replayed evaluation may repeat one
        // written by an earlier dispatch.
        recipients.RemoveAll(id => _queuedThisScope.Contains((id, draft.DedupKey)));
        if (recipients.Count == 0)
            return;

        var alreadyStored = await db.Notifications
            .Where(n => n.DedupKey == draft.DedupKey && recipients.Contains(n.UserId))
            .Select(n => n.UserId)
            .ToListAsync(cancellationToken);
        recipients.RemoveAll(alreadyStored.Contains);
        if (recipients.Count == 0)
            return;

        var now = DateTimeOffset.UtcNow;
        foreach (var userId in recipients)
        {
            db.Notifications.Add(new Notification
            {
                UserId = userId,
                TeamId = draft.TeamId,
                Type = draft.Type,
                CreatedAt = now,
                ActorPlayerId = draft.ActorPlayerId,
                SubjectPlayerId = draft.SubjectPlayerId,
                MatchId = draft.MatchId,
                SeasonId = draft.SeasonId,
                ChatMessageId = draft.ChatMessageId,
                Category = draft.Category,
                Value = draft.Value,
                Emoji = draft.Emoji,
                DedupKey = draft.DedupKey,
            });
            _queuedThisScope.Add((userId, draft.DedupKey));
            events.Enqueue(new NotificationCreatedEvent(userId));
        }
    }

    /// <summary>
    /// Which of a team's users have turned this category off. Rows are sparse — only overrides
    /// exist — so this normally returns nothing and touches a handful of rows at most.
    /// </summary>
    private async Task<HashSet<int>> ResolveMutedAsync(
        int teamId, NotificationCategory category, CancellationToken cancellationToken)
    {
        if (_mutedCache.TryGetValue((teamId, category), out var cached))
            return cached;

        var muted = (await db.NotificationPreferences
                .AsNoTracking()
                .Where(p => p.TeamId == teamId && p.Category == category)
                .Where(p => (p.Channels & NotificationChannel.InApp) == 0)
                .Select(p => p.UserId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        _mutedCache[(teamId, category)] = muted;
        return muted;
    }
}
