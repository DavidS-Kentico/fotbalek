namespace Fotbalek.Domain.Entities;

/// <summary>
/// Frozen pair standings — one row per duo that played at least one match together in the season,
/// written only at season close. Render-time filtering applies the usual minimum
/// (MinGamesForPartnerStats), so the stored data stays reusable if thresholds change.
/// </summary>
public class SeasonPair
{
    public int Id { get; set; }
    public int SeasonId { get; set; }

    /// <summary>Convention: Player1Id &lt; Player2Id.</summary>
    public int Player1Id { get; set; }
    public int Player2Id { get; set; }

    public int MatchesTogether { get; set; }

    /// <summary>Wins by score.</summary>
    public int WinsTogether { get; set; }

    public Season Season { get; set; } = null!;
    public Player Player1 { get; set; } = null!;
    public Player Player2 { get; set; } = null!;
}
