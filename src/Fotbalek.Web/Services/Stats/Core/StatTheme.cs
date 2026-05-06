namespace Fotbalek.Web.Services.Stats.Core;

public enum StatTheme
{
    Rankings,
    Streaks,
    Margins,
    EloSwings,
    Positions,
    Rivalries,
    Partnerships,
    Underdog,
    CareerArc,
    Activity,
    Special
}

public static class StatThemeExtensions
{
    public static string DisplayName(this StatTheme theme) => theme switch
    {
        StatTheme.Rankings => "Rankings",
        StatTheme.Streaks => "Streaks",
        StatTheme.Margins => "Margins",
        StatTheme.EloSwings => "ELO Swings",
        StatTheme.Positions => "Positions",
        StatTheme.Rivalries => "Rivalries",
        StatTheme.Partnerships => "Partnerships",
        StatTheme.Underdog => "Underdog",
        StatTheme.CareerArc => "Career Arc",
        StatTheme.Activity => "Activity",
        StatTheme.Special => "Special",
        _ => theme.ToString()
    };

    public static string Icon(this StatTheme theme) => theme switch
    {
        StatTheme.Rankings => "bi bi-bar-chart",
        StatTheme.Streaks => "bi bi-fire",
        StatTheme.Margins => "bi bi-arrow-down-up",
        StatTheme.EloSwings => "bi bi-graph-up",
        StatTheme.Positions => "bi bi-person-badge",
        StatTheme.Rivalries => "bi bi-shield-shaded",
        StatTheme.Partnerships => "bi bi-people",
        StatTheme.Underdog => "bi bi-trophy",
        StatTheme.CareerArc => "bi bi-graph-up-arrow",
        StatTheme.Activity => "bi bi-calendar-check",
        StatTheme.Special => "bi bi-star",
        _ => "bi bi-circle"
    };
}
