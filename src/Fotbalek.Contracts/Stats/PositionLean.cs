namespace Fotbalek.Contracts.Stats;

/// <summary>
/// Which role a player's record leans towards. A classification, not a label — the UI picks the
/// wording (a preference reads "Flexible" where a comparison reads "Either").
/// </summary>
public enum PositionLean
{
    /// <summary>Not enough games to say.</summary>
    Unknown,
    Goalkeeper,
    Attacker,
    /// <summary>Split evenly between the two roles.</summary>
    Balanced
}
