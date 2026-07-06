using Fotbalek.Web.Data;
using Fotbalek.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Web.Services;

public class TeamMembershipService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<bool> IsMemberAsync(int userId, int teamId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.TeamMemberships.AnyAsync(m => m.UserId == userId && m.TeamId == teamId);
    }

    public async Task<TeamMembership> JoinAsync(int userId, int teamId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
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
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.TeamMemberships
            .Where(m => m.UserId == userId)
            .Include(m => m.Team)
            .OrderBy(m => m.JoinedAt)
            .Select(m => m.Team)
            .ToListAsync();
    }

    /// <summary>One row per membership for the account page: team, captain flag, and the
    /// user's claimed player in that team (null when none is claimed).</summary>
    public record MembershipOverview(Team Team, DateTimeOffset JoinedAt, bool IsCaptain, Player? MyPlayer);

    public async Task<List<MembershipOverview>> GetMembershipOverviewForUserAsync(int userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.TeamMemberships
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.JoinedAt)
            .Select(m => new MembershipOverview(
                m.Team,
                m.JoinedAt,
                m.Team.CaptainUserId == userId,
                // At most one user-Player per team (unique filtered index).
                m.Team.Players.FirstOrDefault(p => p.UserId == userId)))
            .ToListAsync();
    }
}
