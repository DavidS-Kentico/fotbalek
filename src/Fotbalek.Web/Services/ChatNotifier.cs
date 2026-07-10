using System.Collections.Concurrent;

namespace Fotbalek.Web.Services;

/// <summary>
/// In-process pub/sub for team chat, following the <see cref="PresenceTracker"/> pattern:
/// <see cref="ChatService"/> performs the DB write and then raises an event here; every
/// subscribed circuit (the chat dock / conversation on each authenticated page) filters by
/// TeamId and re-renders. No SignalR hub — chat is a handful of messages per minute, not a
/// frame loop, so the existing Blazor circuit carries the render diffs.
///
/// Also owns the two pieces of ephemeral per-user state that must outlive a scoped service:
/// the typing indicator set and the send throttle (a field on the scoped ChatService would be
/// per-circuit, not per-user). Single server instance assumed, like the live game.
/// </summary>
public sealed class ChatNotifier : IDisposable
{
    private readonly ILogger<ChatNotifier> _logger;
    private readonly Timer _typingSweep;

    public ChatNotifier(ILogger<ChatNotifier> logger)
    {
        _logger = logger;
        // Sweep at half the expiry so a stale entry lives at most ~1.5× the expiry window.
        var period = TimeSpan.FromSeconds(Constants.Chat.TypingExpirySeconds / 2.0);
        _typingSweep = new Timer(_ => SweepTyping(), null, period, period);
    }

    /// <summary>(teamId, message)</summary>
    public event Action<int, ChatMessageDto>? MessagePosted;
    /// <summary>(teamId, messageId)</summary>
    public event Action<int, int>? MessageDeleted;
    /// <summary>(teamId, messageId, new body, editedAt) — panels swap the body in place.
    /// Edits raise no banner and don't touch unread.</summary>
    public event Action<int, int, string, DateTimeOffset>? MessageEdited;
    /// <summary>(teamId, messageId, updated reaction summary)</summary>
    public event Action<int, int, List<ChatReactionDto>>? ReactionChanged;
    /// <summary>(teamId, userIds currently typing — subscribers filter out their own id)</summary>
    public event Action<int, IReadOnlyList<int>>? TypingChanged;
    /// <summary>(teamId, userId, new watermark) — lets the same user's other tabs drop their
    /// unread badges after one tab marks read. Subscribers ignore other users' events.</summary>
    public event Action<int, int, int>? ReadStateChanged;

    public void NotifyMessagePosted(int teamId, ChatMessageDto message) =>
        Raise(() => MessagePosted?.Invoke(teamId, message));

    public void NotifyMessageDeleted(int teamId, int messageId) =>
        Raise(() => MessageDeleted?.Invoke(teamId, messageId));

    public void NotifyMessageEdited(int teamId, int messageId, string body, DateTimeOffset editedAt) =>
        Raise(() => MessageEdited?.Invoke(teamId, messageId, body, editedAt));

    public void NotifyReactionChanged(int teamId, int messageId, List<ChatReactionDto> reactions) =>
        Raise(() => ReactionChanged?.Invoke(teamId, messageId, reactions));

    public void NotifyReadStateChanged(int teamId, int userId, int lastReadMessageId) =>
        Raise(() => ReadStateChanged?.Invoke(teamId, userId, lastReadMessageId));

    private void Raise(Action raise)
    {
        try
        {
            raise();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ChatNotifier subscriber threw");
        }
    }

    // ── Typing state (ephemeral, never in the DB) ──────────────────────────────────────

    private readonly ConcurrentDictionary<int, ConcurrentDictionary<int, DateTimeOffset>> _typingByTeam = new();

    /// <summary>Records that a user started/stopped typing and broadcasts the team's typing
    /// set when it changed. Entries auto-expire (see the sweep timer) in case the "stopped"
    /// signal is lost.</summary>
    public void SetTyping(int teamId, int userId, bool isTyping)
    {
        var team = _typingByTeam.GetOrAdd(teamId, _ => new ConcurrentDictionary<int, DateTimeOffset>());
        bool changed;
        if (isTyping)
        {
            changed = !team.ContainsKey(userId);
            team[userId] = DateTimeOffset.UtcNow.AddSeconds(Constants.Chat.TypingExpirySeconds);
        }
        else
        {
            changed = team.TryRemove(userId, out _);
        }

        if (changed)
            BroadcastTyping(teamId, team);
    }

    private void SweepTyping()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (teamId, team) in _typingByTeam)
        {
            var removedAny = false;
            foreach (var (userId, expiry) in team)
            {
                if (expiry <= now && team.TryRemove(userId, out _))
                    removedAny = true;
            }
            if (removedAny)
                BroadcastTyping(teamId, team);
        }
    }

    private void BroadcastTyping(int teamId, ConcurrentDictionary<int, DateTimeOffset> team) =>
        Raise(() => TypingChanged?.Invoke(teamId, team.Keys.ToArray()));

    // ── Send throttle (per user, in-memory soft limit) ─────────────────────────────────

    private readonly ConcurrentDictionary<int, Queue<DateTimeOffset>> _recentSendsByUser = new();

    /// <summary>Sliding-window check-and-record: returns false (and records nothing) when the
    /// user already sent the maximum number of messages inside the window.</summary>
    public bool TryRecordSend(int userId)
    {
        var sends = _recentSendsByUser.GetOrAdd(userId, _ => new Queue<DateTimeOffset>());
        lock (sends)
        {
            var now = DateTimeOffset.UtcNow;
            var windowStart = now.AddSeconds(-Constants.Chat.SendThrottleWindowSeconds);
            while (sends.Count > 0 && sends.Peek() < windowStart)
                sends.Dequeue();

            if (sends.Count >= Constants.Chat.SendThrottleMaxMessages)
                return false;

            sends.Enqueue(now);
            return true;
        }
    }

    public void Dispose() => _typingSweep.Dispose();
}
