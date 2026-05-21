using Fotbalek.Web.Data;
using Fotbalek.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Web.Services;

public class PlayerService(AppDbContext db)
{
    public async Task<List<Player>> GetByTeamAsync(int teamId, bool includeInactive = false)
    {
        var query = db.Players.Where(p => p.TeamId == teamId);
        if (!includeInactive)
            query = query.Where(p => p.IsActive);
        return await query.OrderBy(p => p.Name).ToListAsync();
    }

    public async Task<Player?> GetByIdAsync(int id)
    {
        return await db.Players.FindAsync(id);
    }

    public async Task<Player?> GetByIdWithTeamAsync(int id)
    {
        return await db.Players.Include(p => p.Team).FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<Player?> GetUserPlayerInTeamAsync(int teamId, int userId)
    {
        return await db.Players.FirstOrDefaultAsync(p => p.TeamId == teamId && p.UserId == userId);
    }

    public async Task<List<Player>> GetClaimablePlayersAsync(int teamId)
    {
        return await db.Players
            .Where(p => p.TeamId == teamId && p.UserId == null && p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<Player> CreateAsync(int teamId, string name, int avatarId, int? userId = null)
    {
        var player = new Player
        {
            TeamId = teamId,
            UserId = userId,
            Name = name,
            AvatarId = avatarId,
            Elo = Constants.Elo.DefaultRating,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.Players.Add(player);
        await db.SaveChangesAsync();

        return player;
    }

    /// <summary>
    /// Creates a placeholder player (no associated user). Requires the caller to be the team admin.
    /// </summary>
    public async Task<Player?> CreatePlaceholderAsync(int teamId, int currentUserId, string name, int avatarId)
    {
        var team = await db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == teamId);
        if (team == null || team.AdminUserId != currentUserId)
            return null;
        return await CreateAsync(teamId, name, avatarId, userId: null);
    }

    public async Task<bool> ClaimAsync(int playerId, int teamId, int userId)
    {
        // Defense in depth: the caller must already be a member of the team.
        // The UI gates this, but the service must not trust UI-only checks.
        var isMember = await db.TeamMemberships
            .AnyAsync(m => m.TeamId == teamId && m.UserId == userId);
        if (!isMember) return false;

        // Verify the user does not already have a player in this team.
        var hasPlayer = await db.Players.AnyAsync(p => p.TeamId == teamId && p.UserId == userId);
        if (hasPlayer) return false;

        var player = await GetByIdAsync(playerId);
        if (player == null || player.TeamId != teamId || player.UserId != null)
            return false;

        player.UserId = userId;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return false;
        }
        return true;
    }

    public async Task<bool> UpdateAsync(int playerId, int teamId, int actorUserId, string name, int avatarId)
    {
        var player = await GetByIdAsync(playerId);
        if (player == null || player.TeamId != teamId)
            return false;

        // Authorize: actor is team admin OR actor owns the player.
        var team = await db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == teamId);
        if (team == null) return false;
        var isAdmin = team.AdminUserId == actorUserId;
        var isOwner = player.UserId == actorUserId;
        if (!isAdmin && !isOwner) return false;

        player.Name = name;
        player.AvatarId = avatarId;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> CanDeactivateAsync(int playerId)
    {
        var recentActivityThreshold = DateTimeOffset.UtcNow.AddDays(-Constants.TimeThresholds.RecentActivityDays);
        var hasRecentMatches = await db.MatchPlayers
            .AnyAsync(mp => mp.PlayerId == playerId && mp.Match.PlayedAt >= recentActivityThreshold);
        return !hasRecentMatches;
    }

    public async Task<bool> DeactivateAsync(int playerId, int teamId, int actorUserId)
    {
        var player = await GetByIdAsync(playerId);
        if (player == null || player.TeamId != teamId)
            return false;

        // Authorize: only the team admin may deactivate players.
        var team = await db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == teamId);
        if (team == null || team.AdminUserId != actorUserId)
            return false;

        player.IsActive = false;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ReactivateAsync(int playerId, int teamId, int actorUserId)
    {
        var player = await GetByIdAsync(playerId);
        if (player == null || player.TeamId != teamId)
            return false;

        // Authorize: only the team admin may reactivate players.
        var team = await db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == teamId);
        if (team == null || team.AdminUserId != actorUserId)
            return false;

        player.IsActive = true;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> IsNameTakenAsync(int teamId, string name, int? excludePlayerId = null)
    {
        var normalizedName = name.ToLowerInvariant();
        var query = db.Players.Where(p => p.TeamId == teamId && p.Name.ToLower() == normalizedName);
        if (excludePlayerId.HasValue)
            query = query.Where(p => p.Id != excludePlayerId.Value);
        return await query.AnyAsync();
    }

    public async Task<int> GetActiveCountAsync(int teamId)
    {
        return await db.Players.CountAsync(p => p.TeamId == teamId && p.IsActive);
    }
}
