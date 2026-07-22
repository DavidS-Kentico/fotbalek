namespace Fotbalek.Domain.Entities;

public class MatchPlayer
{
    public int Id { get; set; }
    public int MatchId { get; set; }
    public int PlayerId { get; set; }
    public int TeamNumber { get; set; } // 1 or 2
    public string Position { get; set; } = string.Empty; // "Goalkeeper" or "Attacker"
    public int EloChange { get; set; }
    public int EloBefore { get; set; }
    public int EloAfter { get; set; }

    // Seasonal-ladder mirror of the Elo* fields. Null for off-season matches; cleared whenever
    // the match leaves its season (season delete, EndsAt shrink).
    public int? SeasonEloBefore { get; set; }
    public int? SeasonEloAfter { get; set; }
    public int? SeasonEloChange { get; set; }

    public Match Match { get; set; } = null!;
    public Player Player { get; set; } = null!;
}
