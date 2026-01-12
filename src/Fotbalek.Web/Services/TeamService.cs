using Fotbalek.Web.Data;
using Fotbalek.Web.Data.Entities;
using Fotbalek.Web.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Web.Services;

public class TeamService(AppDbContext db)
{
    public async Task<Team?> GetByCodeNameAsync(string codeName)
    {
        return await db.Teams
            .FirstOrDefaultAsync(t => t.CodeName == codeName.ToLowerInvariant());
    }

    public async Task<Team?> GetByIdAsync(int id)
    {
        return await db.Teams.FindAsync(id);
    }

    public async Task<Team> CreateAsync(string name, string codeName, string password)
    {
        var team = new Team
        {
            Name = name,
            CodeName = codeName.ToLowerInvariant(),
            PasswordHash = PasswordHasher.Hash(password),
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.Teams.Add(team);
        await db.SaveChangesAsync();

        return team;
    }

    public async Task<bool> ValidatePasswordAsync(string codeName, string password)
    {
        var team = await GetByCodeNameAsync(codeName);
        if (team == null) return false;

        return PasswordHasher.Verify(password, team.PasswordHash);
    }

    public async Task<bool> IsCodeNameTakenAsync(string codeName)
    {
        return await db.Teams.AnyAsync(t => t.CodeName == codeName.ToLowerInvariant());
    }
}
