namespace Fotbalek.Web.Helpers;

/// <summary>
/// Date-range helpers for the time period filters. Parameterized by the user's local "today"
/// (from <see cref="Services.TimeZoneService"/>) — never touches server-local time.
/// </summary>
public static class DateFilterHelper
{
    /// <summary>
    /// Gets the date range for a given period selection.
    /// </summary>
    /// <param name="period">The period: "today", "week", "month", "custom", or "all"</param>
    /// <param name="today">The user's local today</param>
    /// <param name="customStartDate">Custom start date (required when period is "custom")</param>
    /// <param name="customEndDate">Custom end date (required when period is "custom")</param>
    /// <returns>A tuple of (startDate, endDate) for the period, or null for "all"</returns>
    public static (DateOnly startDate, DateOnly endDate)? GetDateRange(
        string period,
        DateOnly today,
        DateOnly? customStartDate = null,
        DateOnly? customEndDate = null)
    {
        return period switch
        {
            "today" => (today, today),
            "week" => (GetStartOfWeek(today), today),
            "month" => (GetStartOfMonth(today), today),
            "custom" when customStartDate.HasValue && customEndDate.HasValue =>
                (customStartDate.Value, customEndDate.Value),
            _ => null
        };
    }

    /// <summary>
    /// Gets the start of the current week (Monday).
    /// </summary>
    public static DateOnly GetStartOfWeek(DateOnly date)
    {
        var dayOfWeek = (int)date.DayOfWeek;
        var daysToMonday = dayOfWeek == 0 ? 6 : dayOfWeek - 1; // Sunday = 0, we want Monday as start
        return date.AddDays(-daysToMonday);
    }

    /// <summary>
    /// Gets the start of the month for the given date.
    /// </summary>
    public static DateOnly GetStartOfMonth(DateOnly date)
    {
        return new DateOnly(date.Year, date.Month, 1);
    }

    /// <summary>
    /// Checks if a (local) date falls within the specified period.
    /// </summary>
    public static bool IsDateInPeriod(
        DateOnly date,
        string period,
        DateOnly today,
        DateOnly? customStartDate = null,
        DateOnly? customEndDate = null)
    {
        var range = GetDateRange(period, today, customStartDate, customEndDate);
        if (range == null)
            return true; // "all" period includes everything

        return date >= range.Value.startDate && date <= range.Value.endDate;
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
            "week" => $"This Week ({GetStartOfWeek(today):MMM d} - {today:MMM d})",
            "month" => $"This Month ({GetStartOfMonth(today):MMM d} - {today:MMM d})",
            "custom" when customStartDate.HasValue && customEndDate.HasValue =>
                $"{customStartDate.Value:MMM d} - {customEndDate.Value:MMM d}",
            _ => null
        };
    }
}
