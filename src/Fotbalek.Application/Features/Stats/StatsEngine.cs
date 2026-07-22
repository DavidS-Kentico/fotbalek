using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Features.Stats.Core;
using Fotbalek.Contracts.Stats;
using Fotbalek.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Stats;

/// <summary>
/// Loads team data and runs every registered stat against it. Scoped — rides the dispatch
/// scope's context like any handler dependency.
/// </summary>
public class StatsEngine(IAppDbContext db, StatRegistry registry)
{
    /// <summary>Compute stats for the team across all matches.</summary>
    public async Task<List<StatResult>> GetAllTimeAsync(int teamId)
    {
        var (players, matches) = await LoadAsync(teamId);
        var ctx = new StatContext
        {
            Matches = matches,
            PlayersById = players,
            Ladder = EloLadder.AllTime,
            IsFullScope = true
        };
        return registry.ComputeAll(ctx);
    }

    /// <summary>Compute all-time-ladder stats from an already-filtered list of matches (caller controls the period).</summary>
    public List<StatResult> Compute(IReadOnlyDictionary<int, Player> playersById, IReadOnlyList<Match> filteredMatches, bool isAllTime)
    {
        var ctx = new StatContext
        {
            Matches = filteredMatches,
            PlayersById = playersById,
            Ladder = EloLadder.AllTime,
            IsFullScope = isAllTime
        };
        return registry.ComputeAll(ctx);
    }

    /// <summary>
    /// Compute season-ladder stats: ELO-based badges read the SeasonElo* fields and the player pool
    /// is season participants only. <paramref name="isFullScope"/> is false when a time period
    /// filter narrows the season's matches further.
    /// </summary>
    public List<StatResult> ComputeSeason(
        IReadOnlyDictionary<int, Player> playersById,
        IReadOnlyDictionary<int, SeasonPlayer> seasonPlayersById,
        IReadOnlyList<Match> filteredMatches,
        bool isFullScope)
    {
        var ctx = new StatContext
        {
            Matches = filteredMatches,
            PlayersById = playersById,
            Ladder = EloLadder.Season,
            IsFullScope = isFullScope,
            SeasonPlayersById = seasonPlayersById
        };
        return registry.ComputeAll(ctx);
    }

    /// <summary>Bulk-load the data shared by every stat — players (with current ELO/active flag) and matches with their MatchPlayers.</summary>
    public async Task<(IReadOnlyDictionary<int, Player> Players, IReadOnlyList<Match> Matches)> LoadAsync(int teamId)
    {
        var players = await db.Players
            .AsNoTracking()
            .Where(p => p.TeamId == teamId)
            .ToListAsync();

        var matches = await db.Matches
            .AsNoTracking()
            .Where(m => m.TeamId == teamId)
            .Include(m => m.MatchPlayers)
                .ThenInclude(mp => mp.Player)
            .OrderBy(m => m.PlayedAt)
            .ThenBy(m => m.Id)
            .ToListAsync();

        return (players.ToDictionary(p => p.Id), matches);
    }

    /// <summary>
    /// Season-scoped variant of <see cref="LoadAsync"/>: the season's matches plus the participant
    /// map (PlayerId → SeasonPlayer ladder row).
    /// </summary>
    public async Task<(IReadOnlyDictionary<int, Player> Players, IReadOnlyList<Match> Matches, IReadOnlyDictionary<int, SeasonPlayer> SeasonPlayers)>
        LoadSeasonAsync(int teamId, int seasonId)
    {
        var players = await db.Players
            .AsNoTracking()
            .Where(p => p.TeamId == teamId)
            .ToListAsync();

        var matches = await db.Matches
            .AsNoTracking()
            .Where(m => m.TeamId == teamId && m.SeasonId == seasonId)
            .Include(m => m.MatchPlayers)
                .ThenInclude(mp => mp.Player)
            .OrderBy(m => m.PlayedAt)
            .ThenBy(m => m.Id)
            .ToListAsync();

        var seasonPlayers = await db.SeasonPlayers
            .AsNoTracking()
            .Where(sp => sp.SeasonId == seasonId)
            .ToListAsync();

        return (players.ToDictionary(p => p.Id), matches, seasonPlayers.ToDictionary(sp => sp.PlayerId));
    }
}
