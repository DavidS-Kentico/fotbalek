namespace Fotbalek.Domain.Entities;

public class TeamMembership
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int TeamId { get; set; }
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;

    public AppUser User { get; set; } = null!;
    public Team Team { get; set; } = null!;
}
