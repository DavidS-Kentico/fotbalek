namespace Fotbalek.Web.Data.Entities;

public class Player
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int AvatarId { get; set; } = 1;
    public int Elo { get; set; } = 1000;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Team Team { get; set; } = null!;
    public ICollection<MatchPlayer> MatchPlayers { get; set; } = new List<MatchPlayer>();
}
