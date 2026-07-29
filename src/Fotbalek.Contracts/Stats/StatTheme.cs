namespace Fotbalek.Contracts.Stats;

/// <summary>
/// The family a stat belongs to — a grouping key, not a label. Its heading text and icon are a UI concern.
/// </summary>
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
