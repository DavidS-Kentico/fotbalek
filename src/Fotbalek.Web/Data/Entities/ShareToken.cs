namespace Fotbalek.Web.Data.Entities;

public class ShareToken
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Team Team { get; set; } = null!;
}
