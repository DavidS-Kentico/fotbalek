namespace Fotbalek.Web.Realtime;

/// <summary>
/// Per-circuit chat dock UI state: is-open, the selected conversation, per-team composer
/// drafts, and the unread-count cache. Circuit-scoped (not component state) so navigating
/// between MainLayout and TeamLayout pages — which swaps the layout and rebuilds the dock —
/// restores the same open/selected state instantly. The dock maintains the unread cache by
/// recomputing on ChatNotifier events; the nav team switcher and Home badges render from it
/// and subscribe to <see cref="Changed"/>.
/// </summary>
public class ChatUiState
{
    private readonly Dictionary<int, int> _unreadByTeam = new();
    private readonly Dictionary<int, string> _draftByTeam = new();

    public bool IsOpen { get; set; }
    public int? SelectedTeamId { get; set; }

    /// <summary>Raised whenever the unread cache is replaced.</summary>
    public event Action? Changed;

    public int TotalUnread { get; private set; }

    public int GetUnread(int teamId) => _unreadByTeam.GetValueOrDefault(teamId);

    public void SetUnreadCounts(Dictionary<int, int> counts)
    {
        _unreadByTeam.Clear();
        foreach (var (teamId, count) in counts)
            _unreadByTeam[teamId] = count;
        TotalUnread = counts.Values.Sum();
        Changed?.Invoke();
    }

    /// <summary>Composer draft survives closing the dock or switching teams (in-circuit only).</summary>
    public string GetDraft(int teamId) => _draftByTeam.GetValueOrDefault(teamId, string.Empty);

    public void SetDraft(int teamId, string draft)
    {
        if (string.IsNullOrEmpty(draft))
            _draftByTeam.Remove(teamId);
        else
            _draftByTeam[teamId] = draft;
    }
}
