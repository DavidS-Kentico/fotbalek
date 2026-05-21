using Fotbalek.Web.Data;
using Fotbalek.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Web.Services;

public class TeamMembershipService(AppDbContext db)
{
    public async Task<bool> IsMemberAsync(int userId, int teamId)
    {
        return await db.TeamMemberships.AnyAsync(m => m.UserId == userId && m.TeamId == teamId);
    }

    public async Task<TeamMembership> JoinAsync(int userId, int teamId)
    {
        var existing = await db.TeamMemberships
            .FirstOrDefaultAsync(m => m.UserId == userId && m.TeamId == teamId);
        if (existing != null)
            return existing;

        var membership = new TeamMembership
        {
            UserId = userId,
            TeamId = teamId,
            JoinedAt = DateTimeOffset.UtcNow
        };
        db.TeamMemberships.Add(membership);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Unique-index race: another request created it; reload and return.
            db.Entry(membership).State = EntityState.Detached;
            existing = await db.TeamMemberships
                .FirstOrDefaultAsync(m => m.UserId == userId && m.TeamId == teamId);
            if (existing == null) throw;
            return existing;
        }
        return membership;
    }

    public async Task<List<Team>> GetTeamsForUserAsync(int userId)
    {
        return await db.TeamMemberships
            .Where(m => m.UserId == userId)
            .Include(m => m.Team)
            .OrderBy(m => m.JoinedAt)
            .Select(m => m.Team)
            .ToListAsync();
    }
}
