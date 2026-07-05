using Microsoft.JSInterop;

namespace Fotbalek.Web.Services;

/// <summary>
/// Resolves the user's browser timezone once per circuit via JS interop
/// (<c>Intl.DateTimeFormat().resolvedOptions().timeZone</c>) and converts between instants
/// (<see cref="DateTimeOffset"/>) and the user's local dates. Entities and logic use
/// DateTimeOffset exclusively; local time exists only at this UI boundary.
/// Until the timezone resolves (interop is unavailable during prerender), falls back to UTC.
/// </summary>
public class TimeZoneService(IJSRuntime js)
{
    private TimeZoneInfo _timeZone = TimeZoneInfo.Utc;
    private bool _resolved;

    public TimeZoneInfo TimeZone => _timeZone;

    /// <summary>
    /// Resolves the browser timezone once per circuit. Safe to call from any component's
    /// OnAfterRenderAsync; subsequent calls are no-ops.
    /// </summary>
    public async Task EnsureResolvedAsync()
    {
        if (_resolved) return;
        try
        {
            var ianaId = await js.InvokeAsync<string?>("getBrowserTimeZone");
            if (!string.IsNullOrWhiteSpace(ianaId) &&
                TimeZoneInfo.TryFindSystemTimeZoneById(ianaId, out var tz))
            {
                _timeZone = tz;
            }
        }
        catch
        {
            // Interop unavailable (prerender) or unknown timezone — keep the UTC fallback.
        }
        _resolved = true;
    }

    /// <summary>The user's local "today".</summary>
    public DateOnly Today => ToLocalDate(DateTimeOffset.UtcNow);

    /// <summary>Converts an instant to the user's local calendar date.</summary>
    public DateOnly ToLocalDate(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, _timeZone).DateTime);

    /// <summary>Converts an instant to the user's local wall-clock time.</summary>
    public DateTimeOffset ToLocalTime(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, _timeZone);

    /// <summary>
    /// Interprets a local wall-clock date-time in the user's timezone and returns the
    /// corresponding instant. Guards against DST-skipped times by advancing to a valid one.
    /// </summary>
    public DateTimeOffset LocalDateTimeToInstant(DateTime local)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (_timeZone.IsInvalidTime(local))
        {
            local = local.AddMinutes(30);
        }
        return new DateTimeOffset(local, _timeZone.GetUtcOffset(local));
    }

    /// <summary>
    /// Local date for display, with the wall-clock time appended when it is not midnight
    /// (a season boundary set mid-day must not silently look like a whole-day boundary).
    /// </summary>
    public string FormatLocalDate(DateTimeOffset instant)
    {
        var local = ToLocalTime(instant);
        return local.TimeOfDay == TimeSpan.Zero
            ? local.ToString("MMM d, yyyy")
            : local.ToString("MMM d, yyyy HH:mm");
    }
}
