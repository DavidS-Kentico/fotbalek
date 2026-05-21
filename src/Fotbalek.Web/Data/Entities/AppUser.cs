using Microsoft.AspNetCore.Identity;

namespace Fotbalek.Web.Data.Entities;

public class AppUser : IdentityUser<int>
{
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<Player> Players { get; set; } = new List<Player>();
    public ICollection<TeamMembership> Memberships { get; set; } = new List<TeamMembership>();
    public ICollection<Team> AdministeredTeams { get; set; } = new List<Team>();
}
