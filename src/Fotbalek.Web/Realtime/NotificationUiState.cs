namespace Fotbalek.Web.Realtime;

/// <summary>
/// Per-circuit cache of the bell's unseen count, so the badge survives in-circuit navigation between
/// team pages and any component can render it without its own query.
/// <para>
/// Deliberately small: unlike <see cref="ChatUiState"/> there is no is-open flag to keep here, because
/// <c>wwwroot/js/ui.js</c> owns the panel's open state in the DOM as a <c>.show</c> class — precisely
/// so a Blazor re-render cannot close a menu whose contents update live, which is exactly what an
/// arriving-notification feed needs (AI/notifications.md §9.1, §9.3). The price is that Blazor also
/// cannot observe opening, which the bell handles with an idempotent trigger handler rather than a
/// mirrored flag that would drift on Escape or an outside click (§7.2).
/// </para>
/// </summary>
public class NotificationUiState
{
    /// <summary>Rows that arrived since the user last looked. Always recomputed from the database,
    /// never incremented (§7.2).</summary>
    public int UnseenCount { get; private set; }

    /// <summary>Raised whenever the cached count is replaced.</summary>
    public event Action? Changed;

    public void SetUnseenCount(int count)
    {
        if (UnseenCount == count)
            return;
        UnseenCount = count;
        Changed?.Invoke();
    }
}
