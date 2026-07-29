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

    /// <summary>
    /// Raised by <see cref="RequestOpen"/> so the dock redraws when ANOTHER component opens it — the
    /// notification bell's chat rows do exactly that.
    /// </summary>
    public event Action? OpenRequested;

    /// <summary>
    /// Opens the dock on a team from outside the dock. Setting <see cref="IsOpen"/> and
    /// <see cref="SelectedTeamId"/> directly would change the state and render nothing: the dock is a
    /// SIBLING component, and Blazor only re-renders the component whose handler fired — and this
    /// class raises <see cref="Changed"/> from the unread cache and nowhere else, so nothing would tell
    /// the dock to redraw (AI/notifications.md §5.2).
    /// </summary>
    public void RequestOpen(int teamId)
    {
        IsOpen = true;
        SelectedTeamId = teamId;
        OpenRequested?.Invoke();
    }

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
