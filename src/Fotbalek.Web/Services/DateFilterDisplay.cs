using Fotbalek.Application.Common;

namespace Fotbalek.Web.Services;

/// <summary>
/// Display-string side of the time period filters (English UI text — stays in Web; the range
/// math lives in Application/Common's DateFilterHelper). Parameterized by the user's local
/// "today" from <see cref="TimeZoneService"/> — never touches server-local time.
/// </summary>
public static class DateFilterDisplay
{
    /// <summary>
    /// Formats a (local) date as a match-list day-group heading: "Today", "Yesterday",
    /// the weekday name within the current week, then progressively fuller dates.
    /// Shared by Match History and the dashboard so their groupings read identically.
    /// </summary>
    public static string FormatDayGroup(DateOnly date, DateOnly today)
    {
        if (date == today) return "Today";
        if (date == today.AddDays(-1)) return "Yesterday";

        var startOfWeek = today.AddDays(-(int)today.DayOfWeek);
        if (date >= startOfWeek) return date.ToString("dddd"); // Monday, Tuesday...

        if (date.Year == today.Year) return date.ToString("dddd, MMM d");
        return date.ToString("dddd, MMM d, yyyy");
    }

    /// <summary>
    /// Gets a display description for the period, including date range.
    /// </summary>
    public static string? GetPeriodDescription(
        string period,
        DateOnly today,
        DateOnly? customStartDate = null,
        DateOnly? customEndDate = null)
    {
        return period switch
        {
            "today" => $"Today ({today:MMM d})",
            "week" => $"This Week ({DateFilterHelper.GetStartOfWeek(today):MMM d} - {today:MMM d})",
            "month" => $"This Month ({DateFilterHelper.GetStartOfMonth(today):MMM d} - {today:MMM d})",
            "custom" when customStartDate.HasValue && customEndDate.HasValue =>
                $"{customStartDate.Value:MMM d} - {customEndDate.Value:MMM d}",
            _ => null
        };
    }
}
