namespace Fotbalek.Web.Data.Entities;

public class Team
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CodeName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<Player> Players { get; set; } = new List<Player>();
    public ICollection<Match> Matches { get; set; } = new List<Match>();
    public ICollection<ShareToken> ShareTokens { get; set; } = new List<ShareToken>();
}
