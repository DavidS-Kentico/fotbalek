using Fotbalek.Web.Data;
using Fotbalek.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Web.Services;

public class MatchService(AppDbContext db, EloService eloService)
{
    public async Task<List<Match>> GetByTeamAsync(int teamId, int page = 1, int pageSize = 20)
    {
        return await db.Matches
            .Where(m => m.TeamId == teamId)
            .Include(m => m.MatchPlayers)
                .ThenInclude(mp => mp.Player)
            .OrderByDescending(m => m.PlayedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<int> GetCountByTeamAsync(int teamId)
    {
        return await db.Matches.CountAsync(m => m.TeamId == teamId);
    }

    public async Task<Match?> GetByIdAsync(int id)
    {
        return await db.Matches
            .Include(m => m.MatchPlayers)
                .ThenInclude(mp => mp.Player)
            .FirstOrDefaultAsync(m => m.Id == id);
    }

    public async Task<Match> CreateAsync(
        int teamId,
        int team1GkId, int team1AtkId,
        int team2GkId, int team2AtkId,
        int team1Score, int team2Score)
    {
        // Validate scores
        if (team1Score < 0 || team2Score < 0)
            throw new ArgumentException("Scores cannot be negative");
        if (team1Score > 10 || team2Score > 10)
            throw new ArgumentException("Scores cannot exceed 10");
        if (team1Score == team2Score)
            throw new ArgumentException("Scores cannot be equal (no draws allowed)");
        if (team1Score != 10 && team2Score != 10)
            throw new ArgumentException("At least one team must score 10");

        // Validate all players are unique
        var playerIds = new[] { team1GkId, team1AtkId, team2GkId, team2AtkId };
        if (playerIds.Distinct().Count() != 4)
            throw new ArgumentException("All players must be different");

        // Get players and validate they belong to the team and are active
        var team1Gk = await db.Players.FindAsync(team1GkId) ?? throw new InvalidOperationException("Player not found");
        var team1Atk = await db.Players.FindAsync(team1AtkId) ?? throw new InvalidOperationException("Player not found");
        var team2Gk = await db.Players.FindAsync(team2GkId) ?? throw new InvalidOperationException("Player not found");
        var team2Atk = await db.Players.FindAsync(team2AtkId) ?? throw new InvalidOperationException("Player not found");

        // Validate all players belong to the same team
        if (team1Gk.TeamId != teamId || team1Atk.TeamId != teamId ||
            team2Gk.TeamId != teamId || team2Atk.TeamId != teamId)
            throw new ArgumentException("All players must belong to the team");

        // Validate all players are active
        if (!team1Gk.IsActive || !team1Atk.IsActive || !team2Gk.IsActive || !team2Atk.IsActive)
            throw new ArgumentException("All players must be active");

        var now = DateTimeOffset.UtcNow;

        // Calculate ELO
        var team1Elo = eloService.GetTeamElo(team1Gk.Elo, team1Atk.Elo);
        var team2Elo = eloService.GetTeamElo(team2Gk.Elo, team2Atk.Elo);
        var team1Won = team1Score > team2Score;
        var (change1, change2) = eloService.CalculateEloChange(team1Elo, team2Elo, team1Won);

        // Use a transaction to ensure atomicity
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            // Create match
            var match = new Match
            {
                TeamId = teamId,
                Team1Score = team1Score,
                Team2Score = team2Score,
                PlayedAt = now,
                CreatedAt = DateTimeOffset.UtcNow
            };

            db.Matches.Add(match);

            // Create match players with ELO tracking
            var matchPlayers = new[]
            {
                CreateMatchPlayer(match, team1Gk, 1, Constants.Positions.Goalkeeper, change1),
                CreateMatchPlayer(match, team1Atk, 1, Constants.Positions.Attacker, change1),
                CreateMatchPlayer(match, team2Gk, 2, Constants.Positions.Goalkeeper, change2),
                CreateMatchPlayer(match, team2Atk, 2, Constants.Positions.Attacker, change2),
            };

            db.MatchPlayers.AddRange(matchPlayers);

            // Update player ELOs
            team1Gk.Elo = eloService.ApplyEloChange(team1Gk.Elo, change1);
            team1Atk.Elo = eloService.ApplyEloChange(team1Atk.Elo, change1);
            team2Gk.Elo = eloService.ApplyEloChange(team2Gk.Elo, change2);
            team2Atk.Elo = eloService.ApplyEloChange(team2Atk.Elo, change2);

            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            return match;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private MatchPlayer CreateMatchPlayer(Match match, Player player, int teamNumber, string position, int eloChange)
    {
        return new MatchPlayer
        {
            Match = match,
            PlayerId = player.Id,
            TeamNumber = teamNumber,
            Position = position,
            EloChange = eloChange,
            EloBefore = player.Elo,
            EloAfter = eloService.ApplyEloChange(player.Elo, eloChange)
        };
    }

    public async Task<bool> CanEditOrDeleteAsync(int matchId)
    {
        var (canDelete, _) = await CanDeleteWithReasonAsync(matchId);
        return canDelete;
    }

    public async Task<(bool CanDelete, string? Reason)> CanDeleteWithReasonAsync(int matchId)
    {
        var match = await db.Matches
            .Include(m => m.MatchPlayers)
            .FirstOrDefaultAsync(m => m.Id == matchId);
        if (match == null) return (false, "Match not found");

        var hoursSinceCreation = (DateTimeOffset.UtcNow - match.CreatedAt).TotalHours;
        if (hoursSinceCreation > Constants.TimeThresholds.MatchDeletionWindowHours)
            return (false, $"Matches can only be deleted within {Constants.TimeThresholds.MatchDeletionWindowHours} hours of creation");

        // Check if this is the most recent match for all players involved
        // This ensures ELO reversal won't corrupt subsequent match history
        // We use MatchId for comparison since matches are always created with current time
        foreach (var mp in match.MatchPlayers)
        {
            var hasLaterMatch = await db.MatchPlayers
                .AnyAsync(laterMp =>
                    laterMp.PlayerId == mp.PlayerId &&
                    laterMp.MatchId > match.Id);

            if (hasLaterMatch)
                return (false, "Cannot delete: one or more players have played matches after this one");
        }

        return (true, null);
    }

    public async Task<bool> DeleteAsync(int matchId, int teamId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            // Re-check permissions before delete (inside transaction to prevent race condition)
            var (canDelete, _) = await CanDeleteWithReasonAsync(matchId);
            if (!canDelete)
                return false;

            var match = await GetByIdAsync(matchId);
            if (match == null) return false;

            // Validate match belongs to the authenticated team
            if (match.TeamId != teamId)
                return false;

            // Reverse ELO changes
            foreach (var mp in match.MatchPlayers)
            {
                var player = await db.Players.FindAsync(mp.PlayerId);
                if (player != null)
                {
                    player.Elo = mp.EloBefore;
                }
            }

            db.Matches.Remove(match);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return true;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<List<Match>> GetRecentByTeamAsync(int teamId, int count = 10)
    {
        return await db.Matches
            .Where(m => m.TeamId == teamId)
            .Include(m => m.MatchPlayers)
                .ThenInclude(mp => mp.Player)
            .OrderByDescending(m => m.PlayedAt)
            .Take(count)
            .ToListAsync();
    }

    public async Task<int> GetMatchesThisWeekAsync(int teamId)
    {
        var recentActivityThreshold = DateTimeOffset.UtcNow.AddDays(-Constants.TimeThresholds.RecentActivityDays);
        return await db.Matches
            .CountAsync(m => m.TeamId == teamId && m.PlayedAt >= recentActivityThreshold);
    }

    public async Task<double> GetAverageMatchScoreAsync(int teamId)
    {
        var matches = await db.Matches
            .Where(m => m.TeamId == teamId)
            .Select(m => m.Team1Score + m.Team2Score)
            .ToListAsync();

        return matches.Count > 0 ? matches.Average() : 0;
    }

    public async Task<List<Match>> GetByPlayerAsync(int playerId, int count = 10)
    {
        return await db.Matches
            .Where(m => m.MatchPlayers.Any(mp => mp.PlayerId == playerId))
            .Include(m => m.MatchPlayers)
                .ThenInclude(mp => mp.Player)
            .OrderByDescending(m => m.PlayedAt)
            .Take(count)
            .ToListAsync();
    }

    public async Task<int> GetCountByPlayerAsync(int playerId)
    {
        return await db.Matches
            .CountAsync(m => m.MatchPlayers.Any(mp => mp.PlayerId == playerId));
    }
}
