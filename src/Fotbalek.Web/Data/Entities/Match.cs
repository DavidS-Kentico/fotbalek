namespace Fotbalek.Web.Data.Entities;

public class Match
{
    public int Id { get; set; }
    public int TeamId { get; set; }

    /// <summary>Null = off-season match. FK is NO ACTION (service-managed) — see SeasonService.DeleteAsync.</summary>
    public int? SeasonId { get; set; }

    public int Team1Score { get; set; }
    public int Team2Score { get; set; }
    public DateTimeOffset PlayedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Team Team { get; set; } = null!;
    public Season? Season { get; set; }
    public ICollection<MatchPlayer> MatchPlayers { get; set; } = new List<MatchPlayer>();
}
