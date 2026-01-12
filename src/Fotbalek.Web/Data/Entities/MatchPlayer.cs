namespace Fotbalek.Web.Data.Entities;

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

    public Match Match { get; set; } = null!;
    public Player Player { get; set; } = null!;
}
