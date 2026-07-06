using Fotbalek.Web.Data;
using Fotbalek.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Web.Services;

/// <summary>
/// Season lifecycle: create (with off-season match import), edit, delete, and the close procedure
/// that freezes standings and generates awards. Every operation runs on its own short-lived
/// DbContext (factory pattern). Concurrency model: every write touching a season's matches takes
/// an update lock on the Season row inside its transaction; season create and EndsAt edits
/// additionally take a per-team application lock because they reshape the season timeline
/// (creation has no row to lock yet).
/// </summary>
public class SeasonService(IDbContextFactory<AppDbContext> dbFactory, EloService eloService, ILogger<SeasonService> logger)
{
    // ---------------------------------------------------------------------
    // Queries
    // ---------------------------------------------------------------------

    public async Task<List<Season>> GetByTeamAsync(int teamId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Seasons
            .AsNoTracking()
            .Where(s => s.TeamId == teamId)
            .OrderByDescending(s => s.StartsAt)
            .ThenByDescending(s => s.Id)
            .ToListAsync();
    }

    public async Task<Season?> GetByIdAsync(int seasonId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Seasons.AsNoTracking().FirstOrDefaultAsync(s => s.Id == seasonId);
    }

    /// <summary>The season currently accepting matches, or null. At most one exists per team.</summary>
    public async Task<Season?> GetActiveSeasonAsync(int teamId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var now = DateTimeOffset.UtcNow;
        return await db.Seasons
            .AsNoTracking()
            .Where(s => s.TeamId == teamId && s.ClosedAt == null &&
                        s.StartsAt <= now && (s.EndsAt == null || now < s.EndsAt))
            .FirstOrDefaultAsync();
    }

    /// <summary>The nearest upcoming scheduled season, or null.</summary>
    public async Task<Season?> GetNextScheduledAsync(int teamId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var now = DateTimeOffset.UtcNow;
        return await db.Seasons
            .AsNoTracking()
            .Where(s => s.TeamId == teamId && s.ClosedAt == null && s.StartsAt > now)
            .OrderBy(s => s.StartsAt)
            .FirstOrDefaultAsync();
    }

    /// <summary>Whether the team has any seasons at all (pages fall back to today's behavior when false).</summary>
    public async Task<bool> HasAnySeasonAsync(int teamId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Seasons.AnyAsync(s => s.TeamId == teamId);
    }

    /// <summary>Unassigned matches of the team whose PlayedAt falls within the given period — the import list.</summary>
    public async Task<List<Match>> GetImportCandidatesAsync(int teamId, DateTimeOffset startsAt, DateTimeOffset? endsAt)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Matches
            .AsNoTracking()
            .Where(m => m.TeamId == teamId && m.SeasonId == null &&
                        m.PlayedAt >= startsAt && (endsAt == null || m.PlayedAt < endsAt))
            .Include(m => m.MatchPlayers)
                .ThenInclude(mp => mp.Player)
            .OrderBy(m => m.PlayedAt).ThenBy(m => m.Id)
            .ToListAsync();
    }

    public async Task<int> GetMatchCountAsync(int seasonId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Matches.CountAsync(m => m.SeasonId == seasonId);
    }

    public async Task<Dictionary<int, int>> GetMatchCountsBySeasonAsync(int teamId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Matches
            .Where(m => m.TeamId == teamId && m.SeasonId != null)
            .GroupBy(m => m.SeasonId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
    }

    /// <summary>Seasonal ELO per player for matchmaking. Players without a row default to 1000 at the call site.</summary>
    public async Task<Dictionary<int, int>> GetSeasonEloMapAsync(int seasonId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.SeasonPlayers
            .Where(sp => sp.SeasonId == seasonId)
            .ToDictionaryAsync(sp => sp.PlayerId, sp => sp.Elo);
    }

    public sealed record SeasonChampion(int PlayerId, string Name, int AvatarId, bool IsAwardHolder);

    /// <summary>
    /// Champion per closed season: the Player-gold award holder, or — when the season generated no
    /// awards or no player reached the Player-award minimum — the FinalRank = 1 player from the
    /// frozen standings.
    /// </summary>
    public async Task<Dictionary<int, SeasonChampion>> GetChampionsAsync(int teamId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var standingsChampions = await db.SeasonPlayerResults
            .Where(r => r.SeasonPlayer.Season.TeamId == teamId && r.FinalRank == 1)
            .Select(r => new { r.SeasonPlayer.SeasonId, r.SeasonPlayer.PlayerId, r.SeasonPlayer.Player.Name, r.SeasonPlayer.Player.AvatarId })
            .ToListAsync();

        var awardChampions = await db.SeasonAwards
            .Where(a => a.Season.TeamId == teamId && a.Category == Constants.Seasons.AwardCategories.Player && a.Rank == 1)
            .Select(a => new { a.SeasonId, a.PlayerId, a.Player.Name, a.Player.AvatarId })
            .ToListAsync();

        var result = standingsChampions.ToDictionary(
            x => x.SeasonId,
            x => new SeasonChampion(x.PlayerId, x.Name, x.AvatarId, IsAwardHolder: false));
        foreach (var a in awardChampions)
        {
            result[a.SeasonId] = new SeasonChampion(a.PlayerId, a.Name, a.AvatarId, IsAwardHolder: true);
        }
        return result;
    }

    public async Task<List<SeasonAward>> GetAwardsAsync(int seasonId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.SeasonAwards
            .AsNoTracking()
            .Where(a => a.SeasonId == seasonId)
            .Include(a => a.Player)
            .Include(a => a.PartnerPlayer)
            .OrderBy(a => a.Category).ThenBy(a => a.Rank)
            .ToListAsync();
    }

    /// <summary>All awards a player has earned, with their seasons — the permanent trophy case.</summary>
    public async Task<List<SeasonAward>> GetAwardsForPlayerAsync(int playerId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.SeasonAwards
            .AsNoTracking()
            .Where(a => a.PlayerId == playerId)
            .Include(a => a.Season)
            .Include(a => a.PartnerPlayer)
            .OrderByDescending(a => a.Season.StartsAt)
            .ThenBy(a => a.Rank)
            .ToListAsync();
    }

    // ---------------------------------------------------------------------
    // Create (with import)
    // ---------------------------------------------------------------------

    public async Task<Season> CreateAsync(
        int teamId, int actorUserId,
        string name, string? description,
        DateTimeOffset startsAt, DateTimeOffset? endsAt,
        IReadOnlyCollection<int>? importMatchIds = null)
    {
        (name, description) = ValidateNameAndDescription(name, description);
        if (endsAt != null && endsAt <= startsAt)
            throw new ArgumentException("The end date must be after the start date.");

        Season season;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            await EnsureCaptainAsync(db, teamId, actorUserId);

            await using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                // Creation has no Season row to lock yet, so the overlap and name checks are
                // serialized through a per-team application lock instead (re-validated under it).
                await AcquireTeamTimelineLockAsync(db, teamId);
                await EnsureNameAvailableAsync(db, teamId, name, excludeSeasonId: null);
                await EnsureNoOverlapAsync(db, teamId, excludeSeasonId: null, startsAt, endsAt);

                season = new Season
                {
                    TeamId = teamId,
                    Name = name,
                    Description = description,
                    StartsAt = startsAt,
                    EndsAt = endsAt,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                db.Seasons.Add(season);
                await db.SaveChangesAsync();

                if (importMatchIds is { Count: > 0 })
                {
                    var matches = await db.Matches
                        .Include(m => m.MatchPlayers)
                        .Where(m => importMatchIds.Contains(m.Id))
                        .ToListAsync();

                    foreach (var match in matches)
                    {
                        if (match.TeamId != teamId)
                            throw new ArgumentException("Only matches of this team can be imported.");
                        if (match.SeasonId != null)
                            throw new ArgumentException("Only unassigned matches can be imported.");
                        if (match.PlayedAt < startsAt || (endsAt != null && match.PlayedAt >= endsAt))
                            throw new ArgumentException("Only matches within the season period can be imported.");
                        match.SeasonId = season.Id;
                    }

                    // Replay in chronological order to build the seasonal ladder; all-time ELO is untouched.
                    ReplaySeasonLadder(db, season.Id, existingLadder: [], matches);
                    await db.SaveChangesAsync();
                }

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // A season created entirely in the past is already due — close it on the spot
        // (results and awards generated immediately) instead of waiting for the lazy close.
        if (season.EndsAt != null && season.EndsAt <= DateTimeOffset.UtcNow)
        {
            await CloseSeasonAsync(season.Id);
        }

        return season;
    }

    // ---------------------------------------------------------------------
    // Edit
    // ---------------------------------------------------------------------

    /// <summary>Rename + edit description — allowed anytime, including closed seasons.</summary>
    public async Task UpdateDetailsAsync(int seasonId, int actorUserId, string name, string? description)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var season = await db.Seasons.FirstOrDefaultAsync(s => s.Id == seasonId)
            ?? throw new InvalidOperationException("Season not found.");
        await EnsureCaptainAsync(db, season.TeamId, actorUserId);
        (name, description) = ValidateNameAndDescription(name, description);
        await EnsureNameAvailableAsync(db, season.TeamId, name, excludeSeasonId: seasonId);

        season.Name = name;
        season.Description = description;
        await db.SaveChangesAsync();
    }

    /// <summary>Matches that would be unassigned by shrinking EndsAt — for the confirmation dialog.</summary>
    public async Task<int> CountMatchesBeyondAsync(int seasonId, DateTimeOffset newEndsAt)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Matches.CountAsync(m => m.SeasonId == seasonId && m.PlayedAt >= newEndsAt);
    }

    /// <summary>
    /// Edit EndsAt of a non-closed season. Shrinking past already-assigned matches unassigns the tail
    /// (requires <paramref name="allowUnassign"/> — the UI confirms first) and replays the season
    /// ladder. Extending an ended-pending-close season revives it. Runs under the team timeline lock
    /// plus the season update lock; rejected if the lazy close won the race.
    /// </summary>
    public async Task UpdateEndsAtAsync(int seasonId, int actorUserId, DateTimeOffset? newEndsAt, bool allowUnassign)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var probe = await db.Seasons.AsNoTracking().FirstOrDefaultAsync(s => s.Id == seasonId)
            ?? throw new InvalidOperationException("Season not found.");
        await EnsureCaptainAsync(db, probe.TeamId, actorUserId);

        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            await AcquireTeamTimelineLockAsync(db, probe.TeamId);
            await LockSeasonRowAsync(db, seasonId);

            // Re-read under the lock — the fresh context guarantees committed values.
            var season = await db.Seasons.FirstOrDefaultAsync(s => s.Id == seasonId)
                ?? throw new InvalidOperationException("Season not found.");
            if (season.ClosedAt != null)
                throw new InvalidOperationException("The season is already closed — its results are frozen.");
            if (newEndsAt != null && newEndsAt <= season.StartsAt)
                throw new ArgumentException("The end date must be after the start date.");

            await EnsureNoOverlapAsync(db, season.TeamId, seasonId, season.StartsAt, newEndsAt);

            var seasonMatches = await db.Matches
                .Include(m => m.MatchPlayers)
                .Where(m => m.SeasonId == seasonId)
                .ToListAsync();
            List<Match> tail = newEndsAt == null
                ? []
                : seasonMatches.Where(m => m.PlayedAt >= newEndsAt).ToList();

            if (tail.Count > 0)
            {
                if (!allowUnassign)
                    throw new InvalidOperationException(
                        $"{tail.Count} match(es) were played after the new end date and would become off-season. Confirmation required.");

                foreach (var match in tail)
                {
                    match.SeasonId = null;
                    foreach (var mp in match.MatchPlayers)
                    {
                        mp.SeasonEloBefore = null;
                        mp.SeasonEloAfter = null;
                        mp.SeasonEloChange = null;
                    }
                }

                // Replay the whole season ladder from the remaining matches — the simple,
                // always-correct rollback of the unassigned tail's seasonal ELO.
                var remaining = seasonMatches.Except(tail).ToList();
                var existingLadder = await db.SeasonPlayers.Where(sp => sp.SeasonId == seasonId).ToListAsync();
                ReplaySeasonLadder(db, seasonId, existingLadder, remaining);
            }

            season.EndsAt = newEndsAt;
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    // ---------------------------------------------------------------------
    // End / close
    // ---------------------------------------------------------------------

    /// <summary>Manual / premature end: sets EndsAt to now (if unset or in the future) and closes the season.</summary>
    public async Task EndNowAsync(int seasonId, int actorUserId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var probe = await db.Seasons.AsNoTracking().FirstOrDefaultAsync(s => s.Id == seasonId)
            ?? throw new InvalidOperationException("Season not found.");
        await EnsureCaptainAsync(db, probe.TeamId, actorUserId);

        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            await LockSeasonRowAsync(db, seasonId);
            var season = await db.Seasons.FirstOrDefaultAsync(s => s.Id == seasonId)
                ?? throw new InvalidOperationException("Season not found.");
            if (season.ClosedAt != null)
                throw new InvalidOperationException("The season is already closed.");

            var now = DateTimeOffset.UtcNow;
            if (season.StartsAt > now)
                throw new InvalidOperationException("A scheduled season has not started yet — delete it instead of ending it.");

            if (season.EndsAt == null || season.EndsAt > now)
            {
                season.EndsAt = now;
            }
            await CloseSeasonCoreAsync(db, season, now);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Lazy close (system action, triggered by any member's page load): closes every season of the
    /// team past its end date. More than one can be pending at once (e.g. a backfilled past season
    /// alongside a naturally ended one). Failures are logged, never propagated to the page load.
    /// </summary>
    public async Task CloseDueSeasonsAsync(int teamId)
    {
        var now = DateTimeOffset.UtcNow;
        List<int> dueIds;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            dueIds = await db.Seasons
                .Where(s => s.TeamId == teamId && s.ClosedAt == null && s.EndsAt != null && s.EndsAt <= now)
                .Select(s => s.Id)
                .ToListAsync();
        }

        foreach (var seasonId in dueIds)
        {
            try
            {
                await CloseSeasonAsync(seasonId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Lazy close of season {SeasonId} failed", seasonId);
            }
        }
    }

    /// <summary>
    /// Closes one season, idempotently: re-reads the season and re-checks ClosedAt == null under an
    /// update lock before doing anything; a concurrent loser sees ClosedAt set and does nothing.
    /// </summary>
    public async Task CloseSeasonAsync(int seasonId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            await LockSeasonRowAsync(db, seasonId);
            var season = await db.Seasons.FirstOrDefaultAsync(s => s.Id == seasonId);
            if (season == null) return;
            if (season.ClosedAt != null) return; // concurrent close won — nothing to do

            var now = DateTimeOffset.UtcNow;
            if (season.EndsAt == null || season.EndsAt > now) return; // not actually due

            await CloseSeasonCoreAsync(db, season, now);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    // ---------------------------------------------------------------------
    // Delete
    // ---------------------------------------------------------------------

    /// <summary>
    /// Deletes a season: matches become off-season (SeasonId and SeasonElo* cleared — the FK is
    /// NO ACTION, so this is service-managed), and all season rows including awards are removed
    /// (players lose the achievements from that season). All-time ELO is unaffected.
    /// </summary>
    public async Task DeleteAsync(int seasonId, int actorUserId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var probe = await db.Seasons.AsNoTracking().FirstOrDefaultAsync(s => s.Id == seasonId)
            ?? throw new InvalidOperationException("Season not found.");
        await EnsureCaptainAsync(db, probe.TeamId, actorUserId);

        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            await LockSeasonRowAsync(db, seasonId);
            var season = await db.Seasons.FirstOrDefaultAsync(s => s.Id == seasonId);
            if (season == null) return;

            await db.MatchPlayers
                .Where(mp => mp.Match.SeasonId == seasonId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(mp => mp.SeasonEloBefore, (int?)null)
                    .SetProperty(mp => mp.SeasonEloAfter, (int?)null)
                    .SetProperty(mp => mp.SeasonEloChange, (int?)null));

            await db.Matches
                .Where(m => m.SeasonId == seasonId)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.SeasonId, (int?)null));

            // SeasonPlayer (with its SeasonPlayerResult), SeasonPair and SeasonAward rows cascade.
            db.Seasons.Remove(season);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    // ---------------------------------------------------------------------
    // Close procedure (§8)
    // ---------------------------------------------------------------------

    private async Task CloseSeasonCoreAsync(AppDbContext db, Season season, DateTimeOffset now)
    {
        var matches = await db.Matches
            .Include(m => m.MatchPlayers)
            .Where(m => m.SeasonId == season.Id)
            .OrderBy(m => m.PlayedAt).ThenBy(m => m.Id)
            .ToListAsync();

        // Read-only load — the ladder rows themselves are not touched at close (their Elo is
        // already final), so skip tracking.
        var ladder = await db.SeasonPlayers
            .AsNoTracking()
            .Include(sp => sp.Player)
            .Where(sp => sp.SeasonId == season.Id)
            .ToListAsync();

        // 1. Freeze results — one SeasonPlayerResult per participant, wins by score.
        var aggregates = SeasonAggregates.ComputeParticipants(matches);

        // FinalRank only for players active at close; deterministic tie-breaks:
        // seasonal ELO desc → wins desc → matches played desc → PlayerId asc.
        var rankByPlayer = ladder
            .Where(sp => sp.Player.IsActive)
            .OrderByDescending(sp => sp.Elo)
            .ThenByDescending(sp => aggregates.TryGetValue(sp.PlayerId, out var a) ? a.Wins : 0)
            .ThenByDescending(sp => aggregates.TryGetValue(sp.PlayerId, out var a) ? a.MatchesPlayed : 0)
            .ThenBy(sp => sp.PlayerId)
            .Select((sp, index) => (sp.PlayerId, Rank: index + 1))
            .ToDictionary(x => x.PlayerId, x => x.Rank);

        var participants = new List<ParticipantClose>();
        foreach (var sp in ladder)
        {
            var agg = aggregates.TryGetValue(sp.PlayerId, out var a) ? a : new SeasonAggregates.ParticipantAggregate();
            var result = new SeasonPlayerResult
            {
                SeasonPlayerId = sp.Id,
                FinalRank = rankByPlayer.TryGetValue(sp.PlayerId, out var rank) ? rank : null,
                Wins = agg.Wins,
                Losses = agg.Losses,
                MatchesPlayed = agg.MatchesPlayed,
                LongestWinStreak = agg.LongestWinStreak,
                LongestLossStreak = agg.LongestLossStreak,
                GoalkeeperMatches = agg.GoalkeeperMatches,
                GoalsConcededAsGoalkeeper = agg.GoalsConcededAsGoalkeeper,
                AttackerMatches = agg.AttackerMatches,
                GoalsScoredAsAttacker = agg.GoalsScoredAsAttacker
            };
            db.SeasonPlayerResults.Add(result);
            participants.Add(new ParticipantClose(sp.PlayerId, sp.Elo, result));
        }

        var pairRows = new List<SeasonPair>();
        foreach (var ((player1Id, player2Id), pair) in SeasonAggregates.ComputePairs(matches))
        {
            var row = new SeasonPair
            {
                SeasonId = season.Id,
                Player1Id = player1Id,
                Player2Id = player2Id,
                MatchesTogether = pair.Matches,
                WinsTogether = pair.Wins
            };
            db.SeasonPairs.Add(row);
            pairRows.Add(row);
        }

        // 2. Awards — only if the season has enough matches in total; standings still freeze below that.
        if (matches.Count >= Constants.Seasons.MinMatchesForAwards)
        {
            GenerateAwards(db, season, participants, pairRows);
        }

        // 3. Close.
        season.EndsAt ??= now;
        season.ClosedAt = now;
    }

    /// <summary>PlayerId + final seasonal ELO + the frozen result row. FinalRank != null ⇔ active at close.</summary>
    private sealed record ParticipantClose(int PlayerId, int Elo, SeasonPlayerResult Result);

    private static void GenerateAwards(AppDbContext db, Season season, List<ParticipantClose> participants, List<SeasonPair> pairs)
    {
        var byPlayer = participants.ToDictionary(p => p.PlayerId);

        // Top 3 players: the frozen standings order filtered to the Player-award match minimum —
        // the award champion and the standings leader can therefore disagree.
        var playerPodium = participants
            .Where(p => p.Result.FinalRank != null && p.Result.MatchesPlayed >= Constants.Seasons.MinMatchesForPlayerAward)
            .OrderBy(p => p.Result.FinalRank)
            .Take(3)
            .ToList();
        AddAwards(Constants.Seasons.AwardCategories.Player, playerPodium.Select(p => p.PlayerId));

        // Top 3 goalkeepers: fewest goals conceded per game; same threshold and metric as the rankings.
        var goalkeeperPodium = participants
            .Where(p => p.Result.FinalRank != null && p.Result.GoalkeeperMatches >= Constants.TimeThresholds.MinGamesForPositionBadge)
            .OrderBy(p => (double)p.Result.GoalsConcededAsGoalkeeper / p.Result.GoalkeeperMatches)
            .ThenByDescending(p => p.Result.GoalkeeperMatches)
            .ThenByDescending(p => p.Elo)
            .ThenBy(p => p.PlayerId)
            .Take(3)
            .ToList();
        AddAwards(Constants.Seasons.AwardCategories.Goalkeeper, goalkeeperPodium.Select(p => p.PlayerId));

        // Top 3 attackers: most goals scored per game.
        var attackerPodium = participants
            .Where(p => p.Result.FinalRank != null && p.Result.AttackerMatches >= Constants.TimeThresholds.MinGamesForPositionBadge)
            .OrderByDescending(p => (double)p.Result.GoalsScoredAsAttacker / p.Result.AttackerMatches)
            .ThenByDescending(p => p.Result.AttackerMatches)
            .ThenByDescending(p => p.Elo)
            .ThenBy(p => p.PlayerId)
            .Take(3)
            .ToList();
        AddAwards(Constants.Seasons.AwardCategories.Attacker, attackerPodium.Select(p => p.PlayerId));

        // Top 3 pairs: win rate together; excluded if either member is inactive at close.
        var pairPodium = pairs
            .Where(pr => pr.MatchesTogether >= Constants.TimeThresholds.MinGamesForPartnerStats &&
                         byPlayer.TryGetValue(pr.Player1Id, out var m1) && m1.Result.FinalRank != null &&
                         byPlayer.TryGetValue(pr.Player2Id, out var m2) && m2.Result.FinalRank != null)
            .OrderByDescending(pr => (double)pr.WinsTogether / pr.MatchesTogether)
            .ThenByDescending(pr => pr.MatchesTogether)
            .ThenByDescending(pr => byPlayer[pr.Player1Id].Elo + byPlayer[pr.Player2Id].Elo)
            .ThenBy(pr => Math.Min(pr.Player1Id, pr.Player2Id))
            .Take(3)
            .ToList();

        var pairRank = 1;
        foreach (var pair in pairPodium)
        {
            // One row per member so lookups by PlayerId stay trivial.
            db.SeasonAwards.Add(new SeasonAward
            {
                SeasonId = season.Id,
                PlayerId = pair.Player1Id,
                PartnerPlayerId = pair.Player2Id,
                Category = Constants.Seasons.AwardCategories.Pair,
                Rank = pairRank
            });
            db.SeasonAwards.Add(new SeasonAward
            {
                SeasonId = season.Id,
                PlayerId = pair.Player2Id,
                PartnerPlayerId = pair.Player1Id,
                Category = Constants.Seasons.AwardCategories.Pair,
                Rank = pairRank
            });
            pairRank++;
        }

        void AddAwards(string category, IEnumerable<int> playerIdsInOrder)
        {
            var rank = 1;
            foreach (var playerId in playerIdsInOrder)
            {
                db.SeasonAwards.Add(new SeasonAward
                {
                    SeasonId = season.Id,
                    PlayerId = playerId,
                    Category = category,
                    Rank = rank++
                });
            }
        }
    }

    // ---------------------------------------------------------------------
    // Ladder replay (import §4.1, EndsAt shrink §4.3)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Rebuilds the seasonal ladder from scratch by replaying the given season matches in
    /// chronological order (PlayedAt, ties by Id): SeasonPlayer rows created/reset, seasonal ELO
    /// computed with the same math as live recording (including the rating floor), and the
    /// MatchPlayer.SeasonElo* fields filled. Ladder rows left with zero season matches are deleted.
    /// </summary>
    private void ReplaySeasonLadder(AppDbContext db, int seasonId, List<SeasonPlayer> existingLadder, List<Match> seasonMatches)
    {
        var ladderByPlayer = existingLadder.ToDictionary(sp => sp.PlayerId);
        foreach (var sp in ladderByPlayer.Values)
        {
            sp.Elo = Constants.Elo.DefaultRating;
        }
        var participated = new HashSet<int>();

        foreach (var match in seasonMatches.OrderBy(m => m.PlayedAt).ThenBy(m => m.Id))
        {
            var team1 = match.MatchPlayers.Where(mp => mp.TeamNumber == 1).ToList();
            var team2 = match.MatchPlayers.Where(mp => mp.TeamNumber == 2).ToList();
            if (team1.Count != 2 || team2.Count != 2) continue;

            var ladder1A = GetOrCreateLadderRow(team1[0].PlayerId);
            var ladder1B = GetOrCreateLadderRow(team1[1].PlayerId);
            var ladder2A = GetOrCreateLadderRow(team2[0].PlayerId);
            var ladder2B = GetOrCreateLadderRow(team2[1].PlayerId);

            var team1Elo = eloService.GetTeamElo(ladder1A.Elo, ladder1B.Elo);
            var team2Elo = eloService.GetTeamElo(ladder2A.Elo, ladder2B.Elo);
            var team1Won = match.Team1Score > match.Team2Score;
            var (change1, change2) = eloService.CalculateEloChange(team1Elo, team2Elo, team1Won);

            Apply(team1[0], ladder1A, change1);
            Apply(team1[1], ladder1B, change1);
            Apply(team2[0], ladder2A, change2);
            Apply(team2[1], ladder2B, change2);
        }

        foreach (var sp in ladderByPlayer.Values)
        {
            if (!participated.Contains(sp.PlayerId))
            {
                db.SeasonPlayers.Remove(sp);
            }
        }

        SeasonPlayer GetOrCreateLadderRow(int playerId)
        {
            if (!ladderByPlayer.TryGetValue(playerId, out var sp))
            {
                sp = new SeasonPlayer { SeasonId = seasonId, PlayerId = playerId, Elo = Constants.Elo.DefaultRating };
                db.SeasonPlayers.Add(sp);
                ladderByPlayer[playerId] = sp;
            }
            participated.Add(playerId);
            return sp;
        }

        void Apply(MatchPlayer mp, SeasonPlayer sp, int change)
        {
            mp.SeasonEloBefore = sp.Elo;
            sp.Elo = eloService.ApplyEloChange(sp.Elo, change);
            mp.SeasonEloAfter = sp.Elo;
            mp.SeasonEloChange = change;
        }
    }

    // ---------------------------------------------------------------------
    // Plumbing
    // ---------------------------------------------------------------------

    private static async Task EnsureCaptainAsync(AppDbContext db, int teamId, int actorUserId)
    {
        var team = await db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == teamId)
            ?? throw new InvalidOperationException("Team not found.");
        if (team.CaptainUserId != actorUserId)
            throw new UnauthorizedAccessException("Only the team captain can manage seasons.");
    }

    /// <summary>
    /// Season names are unique per team (case-insensitive, trimmed) — the name is the human
    /// identifier in selectors, match chips and trophy cases, where the period is not shown.
    /// Checked on create (under the team timeline lock) and on rename (excluding the season itself).
    /// </summary>
    private static async Task EnsureNameAvailableAsync(AppDbContext db, int teamId, string name, int? excludeSeasonId)
    {
        var normalized = name.ToLowerInvariant();
        var taken = await db.Seasons.AnyAsync(s =>
            s.TeamId == teamId &&
            (excludeSeasonId == null || s.Id != excludeSeasonId) &&
            s.Name.ToLower() == normalized);
        if (taken)
            throw new ArgumentException($"A season named \"{name}\" already exists in this team.");
    }

    /// <summary>
    /// Season periods [StartsAt, EndsAt) of a team must not overlap. An open-ended season extends
    /// to infinity for this check — so while one is open-ended, no later season can be created;
    /// a season entirely in the past can always be added for backfill.
    /// </summary>
    private static async Task EnsureNoOverlapAsync(AppDbContext db, int teamId, int? excludeSeasonId, DateTimeOffset startsAt, DateTimeOffset? endsAt)
    {
        var blocking = await db.Seasons
            .Where(s => s.TeamId == teamId && (excludeSeasonId == null || s.Id != excludeSeasonId))
            .Where(s => (endsAt == null || s.StartsAt < endsAt) && (s.EndsAt == null || startsAt < s.EndsAt))
            .Select(s => new { s.Name, s.EndsAt })
            .FirstOrDefaultAsync();

        if (blocking != null)
        {
            var hint = blocking.EndsAt == null
                ? " End the current season (or set its end date) first."
                : string.Empty;
            throw new ArgumentException($"The period overlaps season \"{blocking.Name}\".{hint}");
        }
    }

    private static (string Name, string? Description) ValidateNameAndDescription(string name, string? description)
    {
        name = name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 100)
            throw new ArgumentException("The season name must be 1-100 characters.");
        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (description?.Length > 500)
            throw new ArgumentException("The description must be at most 500 characters.");
        return (name, description);
    }

    /// <summary>
    /// Per-team application lock serializing the writes that reshape the season timeline
    /// (create, EndsAt edit). Held until the ambient transaction ends.
    /// </summary>
    private static async Task AcquireTeamTimelineLockAsync(AppDbContext db, int teamId)
    {
        var resource = $"fotbalek-season-timeline-{teamId}";
        await db.Database.ExecuteSqlAsync($@"
DECLARE @lockResult int;
EXEC @lockResult = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
IF @lockResult < 0 THROW 51000, 'Could not acquire the season timeline lock.', 1;");
    }

    /// <summary>
    /// Update lock on the Season row — every write touching a season's matches serializes through it
    /// (EF Core has no pessimistic-locking API, hence raw SQL). Held until the ambient transaction
    /// ends. Also used by MatchService for seasonal match creation and deletion, on that operation's
    /// own context.
    /// </summary>
    public static async Task LockSeasonRowAsync(AppDbContext db, int seasonId)
    {
        await db.Database.ExecuteSqlAsync(
            $"SELECT Id FROM Seasons WITH (UPDLOCK, ROWLOCK) WHERE Id = {seasonId}");
    }
}
