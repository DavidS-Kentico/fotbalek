using Fotbalek.Web.Data;
using Fotbalek.Web.Data.Entities;
using Fotbalek.Web.Services.Stats.Core;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Web.Services.Stats;

/// <summary>
/// Loads team data and runs every registered stat against it. Replaces the monolithic GetBadgesAsync that lived in StatsService.
/// </summary>
public class StatsEngine(AppDbContext db, StatRegistry registry)
{
    /// <summary>Compute stats for the team across all matches.</summary>
    public async Task<List<StatResult>> GetAllTimeAsync(int teamId)
    {
        var (players, matches) = await LoadAsync(teamId);
        var ctx = new StatContext
        {
            Matches = matches,
            PlayersById = players,
            IsAllTime = true
        };
        return registry.ComputeAll(ctx);
    }

    /// <summary>Compute stats from an already-filtered list of matches (caller controls the period).</summary>
    public List<StatResult> Compute(IReadOnlyDictionary<int, Player> playersById, IReadOnlyList<Match> filteredMatches, bool isAllTime)
    {
        var ctx = new StatContext
        {
            Matches = filteredMatches,
            PlayersById = playersById,
            IsAllTime = isAllTime
        };
        return registry.ComputeAll(ctx);
    }

    /// <summary>Bulk-load the data shared by every stat — players (with current ELO/active flag) and matches with their MatchPlayers.</summary>
    public async Task<(IReadOnlyDictionary<int, Player> Players, IReadOnlyList<Match> Matches)> LoadAsync(int teamId)
    {
        var players = await db.Players
            .Where(p => p.TeamId == teamId)
            .ToListAsync();

        var matches = await db.Matches
            .Where(m => m.TeamId == teamId)
            .Include(m => m.MatchPlayers)
                .ThenInclude(mp => mp.Player)
            .OrderBy(m => m.PlayedAt)
            .ThenBy(m => m.Id)
            .ToListAsync();

        return (players.ToDictionary(p => p.Id), matches);
    }
}
