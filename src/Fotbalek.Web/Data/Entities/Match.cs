namespace Fotbalek.Web.Data.Entities;

public class Match
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public int Team1Score { get; set; }
    public int Team2Score { get; set; }
    public DateTimeOffset PlayedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Team Team { get; set; } = null!;
    public ICollection<MatchPlayer> MatchPlayers { get; set; } = new List<MatchPlayer>();
}
