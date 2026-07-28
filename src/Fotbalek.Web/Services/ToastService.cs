using Microsoft.JSInterop;

namespace Fotbalek.Web.Services;

/// <summary>
/// Toast accent. Same vocabulary as the <c>.alert-*</c> / <c>.panel--*</c> / <c>.stat-card--*</c>
/// variants, so "brand = it worked, danger = it didn't" reads the same everywhere in the app.
/// </summary>
public enum ToastVariant
{
    Brand,
    Danger,
    Warning,
    Info,
}

/// <summary>One live toast. Created and mutated only by <see cref="ToastService"/>.</summary>
public sealed class Toast
{
    internal Toast(long id, string message, ToastVariant variant, string icon, TimeSpan lifetime)
    {
        Id = id;
        Message = message;
        Variant = variant;
        Icon = icon;
        Lifetime = lifetime;
    }

    public long Id { get; }
    public string Message { get; }
    public ToastVariant Variant { get; }
    public string Icon { get; }
    public TimeSpan Lifetime { get; internal set; }

    /// <summary>True for the length of the exit animation, so the host can fade the card out
    /// before its node disappears from the render tree.</summary>
    public bool IsLeaving { get; internal set; }

    /// <summary>True while the pointer is over the card: the countdown is frozen.</summary>
    public bool IsPaused { get; internal set; }

    /// <summary>
    /// Bumped whenever the countdown restarts (the same message raised again, or the pointer
    /// leaving the card). The host keys the countdown element on it so the CSS animation
    /// replays — an animation only restarts when the element is new.
    /// </summary>
    public int Generation { get; internal set; }
}

/// <summary>
/// Transient, non-blocking feedback for actions that completed. Circuit-scoped, and read by the
/// single <c>ToastHost</c> that each layout renders.
///
/// When to raise a toast, and when not to — the app is consistent about this:
///   * Toast — an action finished and the result is not self-evident on screen: a player was
///     added, a season was renamed, a match was deleted. Also failures of actions with no form
///     to go back to (a row-level button, a chat reaction).
///   * Inline <c>AlertMessage</c> — anything the user must fix before continuing. Validation and
///     submit errors belong beside the field or inside the dialog that owns them, where they stay
///     put while the user re-reads and corrects. A toast that carries the only copy of an error
///     message and then vanishes is a bug, not a style.
///   * Neither — results the UI already shows plainly: the recorded-match panel on New Match, the
///     generated password on the admin reset dialog, an in-place "Copied!" on a copy button.
/// </summary>
public sealed class ToastService
{
    /// <summary>Errors get the long end — they carry something the user may need to act on.</summary>
    private static readonly TimeSpan SuccessLifetime = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan ErrorLifetime = TimeSpan.FromSeconds(7);

    /// <summary>Must stay in step with the <c>toast-out</c> animation in components/toast.css.</summary>
    private static readonly TimeSpan ExitAnimation = TimeSpan.FromMilliseconds(220);

    private const int MaxVisible = 4;
    private const string HandoffKey = "fotbalekToast";

    private readonly IJSRuntime _js;
    private readonly Lock _gate = new();
    private readonly List<Toast> _toasts = [];
    private readonly Dictionary<long, CancellationTokenSource> _timers = [];
    private long _nextId;

    public ToastService(IJSRuntime js) => _js = js;

    /// <summary>
    /// Immutable snapshot for rendering. Countdowns expire on thread-pool threads, so the host
    /// must never enumerate the live list — it would race a removal mid-render.
    /// </summary>
    public IReadOnlyList<Toast> Current { get; private set; } = [];

    /// <summary>Raised after every change. The host re-renders via <c>InvokeAsync</c>.</summary>
    public event Action? Changed;

    // ── Raising ────────────────────────────────────────────────────────────────────────────

    public void Success(string message, string icon = "bi bi-check-circle-fill") =>
        Show(message, ToastVariant.Brand, icon, SuccessLifetime);

    public void Error(string message, string icon = "bi bi-exclamation-octagon-fill") =>
        Show(message, ToastVariant.Danger, icon, ErrorLifetime);

    public void Warning(string message, string icon = "bi bi-exclamation-triangle-fill") =>
        Show(message, ToastVariant.Warning, icon, ErrorLifetime);

    public void Info(string message, string icon = "bi bi-info-circle-fill") =>
        Show(message, ToastVariant.Info, icon, SuccessLifetime);

    /// <summary>
    /// A refused clipboard write — an older browser, or a non-secure context. Every copy button
    /// in the app reports it through here: the label stays on "Copy" when the write fails, so
    /// without this the click reads as nothing happening at all. Shared wording is deliberate —
    /// it also means a user clicking twice gets one card, not two. <paramref name="fallback"/>
    /// says where the value can be picked up by hand.
    /// </summary>
    public void ClipboardFailed(string fallback = "It's in the field next to the button.") =>
        Error($"Couldn't copy to the clipboard. {fallback}", "bi bi-clipboard-x-fill");

    private void Show(string message, ToastVariant variant, string icon, TimeSpan lifetime)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        Toast toast;
        lock (_gate)
        {
            // The same message raised again restarts its countdown instead of stacking a
            // duplicate: an action retried twice and failing the same way is one problem.
            var existing = _toasts.FirstOrDefault(t =>
                !t.IsLeaving && t.Variant == variant && t.Message == message);
            if (existing != null)
            {
                existing.Lifetime = lifetime;
                existing.Generation++;
                // A paused card stays paused — the pointer is still on it. Starting a countdown
                // here would retire it under the pointer with no bar showing, which is exactly
                // what the freeze exists to prevent; Resume starts the clock instead.
                if (!existing.IsPaused) Restart(existing);
                Publish();
                return;
            }

            toast = new Toast(Interlocked.Increment(ref _nextId), message, variant, icon, lifetime);
            _toasts.Add(toast);

            // Oldest out first, so the message that just arrived is never the one dropped.
            // BeginExit marks rather than removes, hence the IsLeaving filter terminating this.
            while (_toasts.Count(t => !t.IsLeaving) > MaxVisible)
                BeginExit(_toasts.First(t => !t.IsLeaving));

            Restart(toast);
            Publish();
        }
    }

    // ── Dismissal ──────────────────────────────────────────────────────────────────────────

    /// <summary>The close button.</summary>
    public void Dismiss(Toast toast)
    {
        lock (_gate)
        {
            BeginExit(toast);
            Publish();
        }
    }

    /// <summary>Pointer entered the card — freeze the countdown so reading is not a race.</summary>
    public void Pause(Toast toast)
    {
        lock (_gate)
        {
            if (toast.IsLeaving || toast.IsPaused) return;
            toast.IsPaused = true;
            Cancel(toast.Id);
            Publish();
        }
    }

    /// <summary>
    /// Pointer left — the countdown starts over from full rather than resuming from where it
    /// froze. Restarting is what the CSS bar can actually mirror (see <see cref="Toast.Generation"/>),
    /// and it errs toward giving the user more time, not less.
    /// </summary>
    public void Resume(Toast toast)
    {
        lock (_gate)
        {
            if (toast.IsLeaving || !toast.IsPaused) return;
            toast.IsPaused = false;
            toast.Generation++;
            Restart(toast);
            Publish();
        }
    }

    // ── Cross-reload handoff ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Queue a toast to appear after a full page load. Circuit-scoped state does not survive
    /// <c>NavigateTo(forceLoad: true)</c> — which the onboarding flows need, to rebuild the auth
    /// and team context — so the message is parked in <c>sessionStorage</c> and drained by
    /// <c>ToastHost</c> on the next circuit. Await this <em>before</em> navigating.
    /// </summary>
    public async Task ShowAfterReloadAsync(string message, ToastVariant variant = ToastVariant.Brand)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        try
        {
            await _js.InvokeVoidAsync($"{HandoffKey}.stash", message, variant.ToString().ToLowerInvariant());
        }
        catch (Exception e) when (e is JSException or JSDisconnectedException or InvalidOperationException)
        {
            // A toast is never worth failing the navigation it accompanies.
        }
    }

    /// <summary>Called once per circuit by <c>ToastHost</c>, as soon as interop is available.</summary>
    public async Task DrainHandoffAsync()
    {
        HandoffToast[] stashed;
        try
        {
            stashed = await _js.InvokeAsync<HandoffToast[]>($"{HandoffKey}.drain");
        }
        catch (Exception e) when (e is JSException or JSDisconnectedException or InvalidOperationException)
        {
            return;
        }

        foreach (var item in stashed)
        {
            if (string.IsNullOrWhiteSpace(item.Message)) continue;
            var variant = Enum.TryParse<ToastVariant>(item.Variant, ignoreCase: true, out var v)
                ? v
                : ToastVariant.Brand;
            switch (variant)
            {
                case ToastVariant.Danger: Error(item.Message); break;
                case ToastVariant.Warning: Warning(item.Message); break;
                case ToastVariant.Info: Info(item.Message); break;
                default: Success(item.Message); break;
            }
        }
    }

    /// <summary>Wire shape of a parked toast — must match <c>fotbalekToast</c> in wwwroot/js/app.js.</summary>
    private sealed record HandoffToast(string? Message, string? Variant);

    // ── Internals (all called under _gate) ─────────────────────────────────────────────────

    private void BeginExit(Toast toast)
    {
        if (toast.IsLeaving) return;
        Cancel(toast.Id);
        toast.IsLeaving = true;
        _ = RemoveAfterAnimationAsync(toast);
    }

    private async Task RemoveAfterAnimationAsync(Toast toast)
    {
        await Task.Delay(ExitAnimation);
        lock (_gate)
        {
            _toasts.Remove(toast);
            Publish();
        }
    }

    private void Restart(Toast toast)
    {
        Cancel(toast.Id);
        var cts = new CancellationTokenSource();
        _timers[toast.Id] = cts;
        _ = ExpireAsync(toast, cts);
    }

    private async Task ExpireAsync(Toast toast, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(toast.Lifetime, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        lock (_gate)
        {
            // The delay can finish a hair before the Cancel that was meant to stop it — a pointer
            // landing on the card, or the same message raised again. Whoever did that has already
            // installed a different timer or none, so only the card's current one may retire it.
            if (!_timers.TryGetValue(toast.Id, out var current) || !ReferenceEquals(current, cts))
                return;

            BeginExit(toast);
            Publish();
        }
    }

    private void Cancel(long id)
    {
        if (!_timers.Remove(id, out var cts)) return;
        cts.Cancel();
        cts.Dispose();
    }

    private void Publish()
    {
        Current = _toasts.ToArray();
        Changed?.Invoke();
    }
}
