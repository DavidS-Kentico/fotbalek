using Fotbalek.Web.Data;
using Fotbalek.Web.Data.Entities;
using Fotbalek.Web.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Web.Services;

public class TeamService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<Team?> GetByCodeNameAsync(string codeName)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Teams
            .FirstOrDefaultAsync(t => t.CodeName == codeName.ToLowerInvariant());
    }

    public async Task<Team> CreateAsync(string name, string codeName, string password, int adminUserId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var team = new Team
        {
            Name = name,
            CodeName = codeName.ToLowerInvariant(),
            PasswordHash = PasswordHasher.Hash(password),
            AdminUserId = adminUserId,
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
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Teams.AnyAsync(t => t.CodeName == codeName.ToLowerInvariant());
    }

    /// <summary>
    /// Atomically claim the admin role for a team if it currently has no admin
    /// and the caller is a member. Returns true if the caller became admin.
    /// </summary>
    public async Task<bool> TryClaimAdminAsync(int teamId, int userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var isMember = await db.TeamMemberships
            .AnyAsync(m => m.TeamId == teamId && m.UserId == userId);
        if (!isMember) return false;

        var rows = await db.Teams
            .Where(t => t.Id == teamId && t.AdminUserId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.AdminUserId, userId));
        return rows > 0;
    }

    /// <summary>
    /// Updates the team display name. Caller must be the team admin.
    /// </summary>
    public async Task<bool> UpdateNameAsync(int teamId, int actorUserId, string newName)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == teamId);
        if (team == null || team.AdminUserId != actorUserId) return false;
        team.Name = newName;
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Updates the team password. Caller must be the team admin.
    /// </summary>
    public async Task<bool> UpdatePasswordAsync(int teamId, int actorUserId, string newPassword)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == teamId);
        if (team == null || team.AdminUserId != actorUserId) return false;
        team.PasswordHash = PasswordHasher.Hash(newPassword);
        await db.SaveChangesAsync();
        return true;
    }
}
