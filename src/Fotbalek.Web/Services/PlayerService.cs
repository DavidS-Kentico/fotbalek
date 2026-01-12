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

    public async Task<Player> CreateAsync(int teamId, string name, int avatarId)
    {
        var player = new Player
        {
            TeamId = teamId,
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

    public async Task<bool> UpdateAsync(int playerId, int teamId, string name, int avatarId)
    {
        var player = await GetByIdAsync(playerId);
        if (player == null || player.TeamId != teamId)
            return false;

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

    public async Task<bool> DeactivateAsync(int playerId, int teamId)
    {
        var player = await GetByIdAsync(playerId);
        if (player == null || player.TeamId != teamId)
            return false;

        player.IsActive = false;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ReactivateAsync(int playerId, int teamId)
    {
        var player = await GetByIdAsync(playerId);
        if (player == null || player.TeamId != teamId)
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
