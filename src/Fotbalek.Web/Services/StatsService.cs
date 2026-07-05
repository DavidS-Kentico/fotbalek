using Fotbalek.Web.Data;
using Fotbalek.Web.Data.Entities;
using Fotbalek.Web.Services.Stats.Activity;
using Fotbalek.Web.Services.Stats.Core;
using Fotbalek.Web.Services.Stats.Special;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Web.Services;

public class StatsService(IDbContextFactory<AppDbContext> dbFactory)
{
    /// <summary>
    /// Per-player aggregates, parameterized by (match subset, ladder): with a <paramref name="seasonId"/>
    /// only that season's matches count and every ELO-based figure reads the SeasonElo* fields;
    /// otherwise all matches and the all-time ladder. Wins are determined by score in both scopes.
    /// </summary>
    public async Task<PlayerStats> GetPlayerStatsAsync(int playerId, int? seasonId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var matchPlayers = await db.MatchPlayers
            .Where(mp => mp.PlayerId == playerId && (seasonId == null || mp.Match.SeasonId == seasonId))
            .Include(mp => mp.Match)
                .ThenInclude(m => m.MatchPlayers)
                    .ThenInclude(mp => mp.Player)
            .OrderBy(mp => mp.Match.PlayedAt)
            .ToListAsync();

        var player = await db.Players.FindAsync(playerId);
        if (player == null)
            return new PlayerStats();

        // Ladder accessors: seasonal fields in season scope, classic fields otherwise.
        var seasonScope = seasonId != null;
        int EloBeforeOf(MatchPlayer mp) => seasonScope ? mp.SeasonEloBefore ?? Constants.Elo.DefaultRating : mp.EloBefore;
        int EloAfterOf(MatchPlayer mp) => seasonScope ? mp.SeasonEloAfter ?? Constants.Elo.DefaultRating : mp.EloAfter;
        int EloChangeOf(MatchPlayer mp) => seasonScope ? mp.SeasonEloChange ?? 0 : mp.EloChange;

        var currentElo = player.Elo;
        if (seasonScope)
        {
            // Seasonal ELO from the ladder row; default 1000 when the player has no seasonal match.
            currentElo = await db.SeasonPlayers
                .Where(sp => sp.SeasonId == seasonId && sp.PlayerId == playerId)
                .Select(sp => (int?)sp.Elo)
                .FirstOrDefaultAsync() ?? Constants.Elo.DefaultRating;
        }

        var stats = new PlayerStats
        {
            CurrentElo = currentElo,
            TotalMatches = matchPlayers.Count
        };

        if (matchPlayers.Count == 0)
        {
            stats.HighestElo = currentElo;
            stats.LowestElo = currentElo;
            return stats;
        }

        // ELO history - consider all ELO values including current
        var allEloValues = matchPlayers.Select(EloBeforeOf)
            .Concat(matchPlayers.Select(EloAfterOf))
            .Append(currentElo);
        stats.HighestElo = allEloValues.Max();
        stats.LowestElo = allEloValues.Min();

        // Calculate streaks and position stats using helper
        var streakResult = CalculateStreaksAndPositionStats(matchPlayers);

        stats.Wins = streakResult.Wins;
        stats.Losses = streakResult.Losses;
        stats.CurrentStreak = streakResult.CurrentStreak;
        stats.LongestWinStreak = streakResult.LongestWinStreak;
        stats.LongestLossStreak = streakResult.LongestLossStreak;
        stats.UnderTableCount = streakResult.UnderTableCount;
        stats.TableSenderCount = streakResult.TableSenderCount;
        stats.GamesAsGk = streakResult.GoalkeeperCount;
        stats.GamesAsAtk = streakResult.AttackerCount;
        stats.WinsAsGk = streakResult.WinsAsGk;
        stats.WinsAsAtk = streakResult.WinsAsAtk;
        stats.WinRateAsGk = streakResult.GoalkeeperCount > 0
            ? (double)streakResult.WinsAsGk / streakResult.GoalkeeperCount * 100
            : 0;
        stats.WinRateAsAtk = streakResult.AttackerCount > 0
            ? (double)streakResult.WinsAsAtk / streakResult.AttackerCount * 100
            : 0;
        stats.GoalsScoredAsGk = streakResult.GoalsScoredAsGk;
        stats.GoalsConcededAsGk = streakResult.GoalsConcededAsGk;
        stats.GoalsScoredAsAtk = streakResult.GoalsScoredAsAtk;
        stats.GoalsConcededAsAtk = streakResult.GoalsConcededAsAtk;

        stats.WinRate = stats.TotalMatches > 0 ? (double)stats.Wins / stats.TotalMatches * 100 : 0;

        // Preferred position
        var totalPositions = streakResult.GoalkeeperCount + streakResult.AttackerCount;
        if (totalPositions > 0)
        {
            var gkRatio = (double)streakResult.GoalkeeperCount / totalPositions;
            if (gkRatio > 0.6)
                stats.PreferredPosition = Constants.Positions.Goalkeeper;
            else if (gkRatio < 0.4)
                stats.PreferredPosition = Constants.Positions.Attacker;
            else
                stats.PreferredPosition = "Flexible";
        }

        // Better position - the role with the higher win rate (min 3 games per position to compare).
        var minGames = Constants.TimeThresholds.MinGamesForPartnerStats;
        var hasGkData = streakResult.GoalkeeperCount >= minGames;
        var hasAtkData = streakResult.AttackerCount >= minGames;

        if (hasGkData && hasAtkData)
        {
            var diff = stats.WinRateAsGk - stats.WinRateAsAtk;
            if (Math.Abs(diff) < 5)
                stats.BetterPosition = "Either";
            else
                stats.BetterPosition = diff > 0 ? Constants.Positions.Goalkeeper : Constants.Positions.Attacker;
        }
        else if (hasGkData)
        {
            stats.BetterPosition = Constants.Positions.Goalkeeper;
        }
        else if (hasAtkData)
        {
            stats.BetterPosition = Constants.Positions.Attacker;
        }

        // Partner stats (full list; min-games filtering happens at display time)
        var partnerStats = matchPlayers
            .SelectMany(mp => mp.Match.MatchPlayers
                .Where(p => p.TeamNumber == mp.TeamNumber && p.PlayerId != playerId)
                .Select(p => new { Partner = p, Won = mp.IsWinner(), OwnEloChange = EloChangeOf(mp) }))
            .GroupBy(x => x.Partner.PlayerId)
            .Select(g => new RelationshipStat
            {
                PlayerId = g.Key,
                Name = g.First().Partner.Player.Name,
                AvatarId = g.First().Partner.Player.AvatarId,
                Games = g.Count(),
                Wins = g.Count(x => x.Won),
                AvgEloChange = g.Average(x => (double)x.OwnEloChange)
            })
            .OrderByDescending(p => p.Games)
            .ToList();

        foreach (var p in partnerStats) p.WinRate = p.Games > 0 ? (double)p.Wins / p.Games * 100 : 0;
        stats.Partners = partnerStats;

        var qualifyingPartners = partnerStats
            .Where(p => p.Games >= Constants.TimeThresholds.MinGamesForPartnerStats)
            .ToList();
        if (qualifyingPartners.Count != 0)
        {
            var best = qualifyingPartners.OrderByDescending(p => p.WinRate).First();
            stats.BestPartner = best.Name;
            stats.BestPartnerWinRate = best.WinRate;
            stats.BestPartnerGames = best.Games;

            if (qualifyingPartners.Count > 1)
            {
                var worst = qualifyingPartners.OrderBy(p => p.WinRate).First();
                stats.WorstPartner = worst.Name;
                stats.WorstPartnerWinRate = worst.WinRate;
                stats.WorstPartnerGames = worst.Games;
            }
        }

        // Enemy stats (full list; win counted from the player's perspective, by score)
        var enemyStats = matchPlayers
            .SelectMany(mp => mp.Match.MatchPlayers
                .Where(p => p.TeamNumber != mp.TeamNumber)
                .Select(p => new { Enemy = p, Won = mp.IsWinner(), OwnEloChange = EloChangeOf(mp) }))
            .GroupBy(x => x.Enemy.PlayerId)
            .Select(g => new RelationshipStat
            {
                PlayerId = g.Key,
                Name = g.First().Enemy.Player.Name,
                AvatarId = g.First().Enemy.Player.AvatarId,
                Games = g.Count(),
                Wins = g.Count(x => x.Won),
                AvgEloChange = g.Average(x => (double)x.OwnEloChange)
            })
            .OrderByDescending(e => e.Games)
            .ToList();

        foreach (var e in enemyStats) e.WinRate = e.Games > 0 ? (double)e.Wins / e.Games * 100 : 0;
        stats.Enemies = enemyStats;

        var qualifyingEnemies = enemyStats
            .Where(e => e.Games >= Constants.TimeThresholds.MinGamesForPartnerStats)
            .ToList();
        if (qualifyingEnemies.Count != 0)
        {
            var easiest = qualifyingEnemies.OrderByDescending(e => e.WinRate).First();
            stats.EasiestEnemy = easiest.Name;
            stats.EasiestEnemyWinRate = easiest.WinRate;
            stats.EasiestEnemyGames = easiest.Games;

            if (qualifyingEnemies.Count > 1)
            {
                var hardest = qualifyingEnemies.OrderBy(e => e.WinRate).First();
                stats.HardestEnemy = hardest.Name;
                stats.HardestEnemyWinRate = hardest.WinRate;
                stats.HardestEnemyGames = hardest.Games;
            }
        }

        // Average ELO change on wins / losses (win/loss by score, change from the selected ladder)
        var winningMps = matchPlayers.Where(mp => mp.IsWinner()).ToList();
        var losingMps = matchPlayers.Where(mp => !mp.IsWinner()).ToList();
        stats.AvgEloChangeOnWin = winningMps.Count > 0 ? winningMps.Average(EloChangeOf) : 0;
        stats.AvgEloChangeOnLoss = losingMps.Count > 0 ? losingMps.Average(EloChangeOf) : 0;

        // Average opponent / teammate ELO (using pre-match ELO of the selected ladder)
        var opponentElos = matchPlayers
            .SelectMany(mp => mp.Match.MatchPlayers
                .Where(p => p.TeamNumber != mp.TeamNumber)
                .Select(EloBeforeOf))
            .ToList();
        stats.AvgOpponentElo = opponentElos.Count > 0 ? (int)Math.Round(opponentElos.Average()) : 0;

        var teammateElos = matchPlayers
            .SelectMany(mp => mp.Match.MatchPlayers
                .Where(p => p.TeamNumber == mp.TeamNumber && p.PlayerId != playerId)
                .Select(EloBeforeOf))
            .ToList();
        stats.AvgTeammateElo = teammateElos.Count > 0 ? (int)Math.Round(teammateElos.Average()) : 0;

        // Recent form: last 10 matches (matchPlayers is ordered ascending by PlayedAt)
        var recent = matchPlayers.TakeLast(10).ToList();
        stats.RecentForm = recent.Select(mp => mp.IsWinner()).ToList();
        stats.RecentFormWinRate = recent.Count > 0
            ? (double)stats.RecentForm.Count(w => w) / recent.Count * 100
            : 0;

        // Teammate variety — Pielou's evenness against the current active roster.
        // Single source of truth: VarietyPlayerStat.ComputeEvenness.
        var activeTeammateIds = await db.Players
            .Where(p => p.TeamId == player.TeamId && p.IsActive && p.Id != playerId)
            .Select(p => p.Id)
            .ToListAsync();

        var activeTeammateIdSet = activeTeammateIds.ToHashSet();
        var gamesPerActivePartner = matchPlayers
            .SelectMany(mp => mp.Match.MatchPlayers
                .Where(p => p.TeamNumber == mp.TeamNumber && p.PlayerId != playerId))
            .Where(p => activeTeammateIdSet.Contains(p.PlayerId))
            .GroupBy(p => p.PlayerId)
            .ToDictionary(g => g.Key, g => g.Count());

        stats.UniqueTeammates = gamesPerActivePartner.Count;
        stats.ActiveRosterPartners = activeTeammateIds.Count;
        var teammateGames = gamesPerActivePartner.Values.Sum();
        stats.TeammateVariety = teammateGames >= Constants.TimeThresholds.MinGamesForVarietyBadge
            ? VarietyPlayerStat.ComputeEvenness(gamesPerActivePartner, activeTeammateIds.Count)
            : 0;
        stats.HasEnoughGamesForVariety = teammateGames >= Constants.TimeThresholds.MinGamesForVarietyBadge;

        // Carry / Carried counts — uses the same predicate as CarriedStat (single source of truth).
        foreach (var mp in matchPlayers)
        {
            if (!mp.Match.TryGetTeams(out var winners, out var losers)) continue;
            var carry = CarriedStat.AnalyzeCarry(winners[0], winners[1], losers[0], losers[1], EloBeforeOf);
            if (carry is null) continue;
            if (carry.Value.CarriedId == playerId) stats.CarriedCount++;
            if (carry.Value.CarrierId == playerId) stats.CarryCount++;
        }

        // ELO history for chart
        stats.EloHistory = matchPlayers
            .Select(mp => new EloHistoryPoint
            {
                Date = mp.Match.PlayedAt,
                Elo = EloAfterOf(mp)
            })
            .ToList();

        // Per-match derived: expected wins, opponent-strength buckets, goal margins, clean sheets,
        // day-of-week activity.
        const int StrongerThreshold = 50; // ELO points
        double expectedWins = 0;
        int gamesVsStronger = 0, winsVsStronger = 0;
        int gamesVsWeaker = 0, winsVsWeaker = 0;
        double winMarginSum = 0; int winMarginCount = 0;
        double lossMarginSum = 0; int lossMarginCount = 0;
        int cleanSheetsAsGk = 0;
        var dowGames = new int[7];
        var dowWins = new int[7];

        foreach (var mp in matchPlayers)
        {
            var opp = mp.Match.MatchPlayers.Where(p => p.TeamNumber != mp.TeamNumber).ToList();
            if (opp.Count == 0) continue;

            var teammate = mp.Match.MatchPlayers
                .FirstOrDefault(p => p.TeamNumber == mp.TeamNumber && p.PlayerId != playerId);
            var teamAvg = teammate != null ? (EloBeforeOf(mp) + EloBeforeOf(teammate)) / 2.0 : EloBeforeOf(mp);
            var oppAvg = opp.Average(p => (double)EloBeforeOf(p));
            var expected = 1.0 / (1.0 + Math.Pow(10, (oppAvg - teamAvg) / 400.0));
            expectedWins += expected;

            var won = mp.IsWinner();
            if (oppAvg - EloBeforeOf(mp) >= StrongerThreshold)
            {
                gamesVsStronger++;
                if (won) winsVsStronger++;
            }
            else if (EloBeforeOf(mp) - oppAvg >= StrongerThreshold)
            {
                gamesVsWeaker++;
                if (won) winsVsWeaker++;
            }

            var ownScore = mp.TeamNumber == 1 ? mp.Match.Team1Score : mp.Match.Team2Score;
            var oppScore = mp.TeamNumber == 1 ? mp.Match.Team2Score : mp.Match.Team1Score;
            var margin = ownScore - oppScore;
            if (won) { winMarginSum += margin; winMarginCount++; }
            else { lossMarginSum += -margin; lossMarginCount++; }

            if (mp.Position == Constants.Positions.Goalkeeper && oppScore == 0)
                cleanSheetsAsGk++;

            var dow = (int)mp.Match.PlayedAt.DayOfWeek;
            dowGames[dow]++;
            if (won) dowWins[dow]++;
        }

        stats.ExpectedWins = expectedWins;
        stats.GamesVsStronger = gamesVsStronger;
        stats.WinsVsStronger = winsVsStronger;
        stats.WinRateVsStronger = gamesVsStronger > 0 ? (double)winsVsStronger / gamesVsStronger * 100 : 0;
        stats.GamesVsWeaker = gamesVsWeaker;
        stats.WinsVsWeaker = winsVsWeaker;
        stats.WinRateVsWeaker = gamesVsWeaker > 0 ? (double)winsVsWeaker / gamesVsWeaker * 100 : 0;
        stats.AvgWinMargin = winMarginCount > 0 ? winMarginSum / winMarginCount : 0;
        stats.AvgLossMargin = lossMarginCount > 0 ? lossMarginSum / lossMarginCount : 0;
        stats.CleanSheetsAsGk = cleanSheetsAsGk;
        stats.MatchesByDayOfWeek = Enumerable.Range(0, 7)
            .Select(d => new DayOfWeekStat((DayOfWeek)d, dowGames[d], dowWins[d]))
            .ToList();

        // Matches per month (last 12 months, oldest first)
        var nowUtc = DateTimeOffset.UtcNow;
        var months = new List<ActivityMonth>(12);
        for (int i = 11; i >= 0; i--)
        {
            var d = nowUtc.AddMonths(-i);
            var games = matchPlayers.Count(mp =>
                mp.Match.PlayedAt.Year == d.Year && mp.Match.PlayedAt.Month == d.Month);
            months.Add(new ActivityMonth(d.Year, d.Month, games));
        }
        stats.MatchesByMonth = months;

        // Milestones
        stats.FirstMatchDate = matchPlayers.FirstOrDefault()?.Match.PlayedAt;
        if (matchPlayers.Count > 0)
        {
            var biggestGain = matchPlayers.OrderByDescending(EloChangeOf).First();
            stats.BiggestEloGain = EloChangeOf(biggestGain);
            stats.BiggestEloGainDate = biggestGain.Match.PlayedAt;

            var biggestLoss = matchPlayers.OrderBy(EloChangeOf).First();
            stats.BiggestEloLoss = EloChangeOf(biggestLoss);
            stats.BiggestEloLossDate = biggestLoss.Match.PlayedAt;

            var peak = matchPlayers.OrderByDescending(EloAfterOf).First();
            stats.PeakEloDate = peak.Match.PlayedAt;
        }

        return stats;
    }

    public async Task<List<PlayerRanking>> GetRankingsAsync(int teamId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        // Fix N+1: Load all data in a single query with grouping
        var players = await db.Players
            .Where(p => p.TeamId == teamId && p.IsActive)
            .OrderByDescending(p => p.Elo)
            .ToListAsync();

        if (players.Count == 0)
            return [];

        var playerIds = players.Select(p => p.Id).ToList();

        // Get all match stats in one query
        var matchStats = await db.MatchPlayers
            .Where(mp => playerIds.Contains(mp.PlayerId))
            .GroupBy(mp => mp.PlayerId)
            .Select(g => new
            {
                PlayerId = g.Key,
                MatchCount = g.Count(),
                Wins = g.Count(mp => mp.EloChange > 0)
            })
            .ToDictionaryAsync(x => x.PlayerId);

        var rankings = new List<PlayerRanking>();
        var rank = 1;

        foreach (var player in players)
        {
            var stats = matchStats.GetValueOrDefault(player.Id);
            var matchCount = stats?.MatchCount ?? 0;
            var wins = stats?.Wins ?? 0;

            rankings.Add(new PlayerRanking
            {
                Rank = rank++,
                PlayerId = player.Id,
                PlayerName = player.Name,
                AvatarId = player.AvatarId,
                Elo = player.Elo,
                Matches = matchCount,
                Wins = wins,
                WinRate = matchCount > 0 ? (double)wins / matchCount * 100 : 0
            });
        }

        return rankings;
    }

    /// <summary>
    /// Season standings. Active seasons aggregate on the fly from season matches (wins by score,
    /// active players only, ranked by seasonal ELO with the deterministic tie-breaks). Closed
    /// seasons render entirely from the frozen tables — zero aggregation; participants with
    /// FinalRank == null (inactive at close) are hidden.
    /// </summary>
    public async Task<List<SeasonStandingRow>> GetSeasonStandingsAsync(Season season)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (season.IsClosed)
        {
            var frozen = await db.SeasonPlayerResults
                .Where(r => r.SeasonPlayer.SeasonId == season.Id && r.FinalRank != null)
                .OrderBy(r => r.FinalRank)
                .Select(r => new SeasonStandingRow
                {
                    Rank = r.FinalRank!.Value,
                    PlayerId = r.SeasonPlayer.PlayerId,
                    PlayerName = r.SeasonPlayer.Player.Name,
                    AvatarId = r.SeasonPlayer.Player.AvatarId,
                    Elo = r.SeasonPlayer.Elo,
                    Matches = r.MatchesPlayed,
                    Wins = r.Wins,
                    Losses = r.Losses,
                    LongestWinStreak = r.LongestWinStreak,
                    LongestLossStreak = r.LongestLossStreak
                })
                .ToListAsync();
            foreach (var row in frozen)
            {
                row.WinRate = row.Matches > 0 ? (double)row.Wins / row.Matches * 100 : 0;
            }
            return frozen;
        }

        var (ladder, aggregates) = await LoadLiveSeasonAggregatesAsync(db, season.Id);

        // Live ladder: seasonal ELO desc → wins desc → matches played desc → PlayerId asc.
        var rows = ladder
            .Where(sp => sp.Player.IsActive)
            .OrderByDescending(sp => sp.Elo)
            .ThenByDescending(sp => aggregates.TryGetValue(sp.PlayerId, out var a) ? a.Wins : 0)
            .ThenByDescending(sp => aggregates.TryGetValue(sp.PlayerId, out var a) ? a.MatchesPlayed : 0)
            .ThenBy(sp => sp.PlayerId)
            .Select((sp, index) =>
            {
                var agg = aggregates.TryGetValue(sp.PlayerId, out var a) ? a : new SeasonAggregates.ParticipantAggregate();
                return new SeasonStandingRow
                {
                    Rank = index + 1,
                    PlayerId = sp.PlayerId,
                    PlayerName = sp.Player.Name,
                    AvatarId = sp.Player.AvatarId,
                    Elo = sp.Elo,
                    Matches = agg.MatchesPlayed,
                    Wins = agg.Wins,
                    Losses = agg.Losses,
                    WinRate = agg.MatchesPlayed > 0 ? (double)agg.Wins / agg.MatchesPlayed * 100 : 0,
                    LongestWinStreak = agg.LongestWinStreak,
                    LongestLossStreak = agg.LongestLossStreak
                };
            })
            .ToList();
        return rows;
    }

    /// <summary>
    /// Season position tables — the same goals-per-game metric and thresholds as the awards, with
    /// the award tie-break chain, so the podium always matches what the tables show. Frozen data
    /// for closed seasons, on-the-fly aggregation for the active one.
    /// </summary>
    public async Task<(List<PositionRanking> Goalkeepers, List<PositionRanking> Attackers)> GetSeasonPositionRankingsAsync(Season season)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        List<(int PlayerId, string Name, int AvatarId, int Elo, SeasonAggregates.ParticipantAggregate Agg)> participants;

        if (season.IsClosed)
        {
            var frozen = await db.SeasonPlayerResults
                .Where(r => r.SeasonPlayer.SeasonId == season.Id && r.FinalRank != null)
                .Select(r => new
                {
                    r.SeasonPlayer.PlayerId,
                    r.SeasonPlayer.Player.Name,
                    r.SeasonPlayer.Player.AvatarId,
                    r.SeasonPlayer.Elo,
                    r.GoalkeeperMatches,
                    r.GoalsConcededAsGoalkeeper,
                    r.AttackerMatches,
                    r.GoalsScoredAsAttacker
                })
                .ToListAsync();

            participants = frozen.Select(r =>
            {
                var agg = new SeasonAggregates.ParticipantAggregate
                {
                    GoalkeeperMatches = r.GoalkeeperMatches,
                    GoalsConcededAsGoalkeeper = r.GoalsConcededAsGoalkeeper,
                    AttackerMatches = r.AttackerMatches,
                    GoalsScoredAsAttacker = r.GoalsScoredAsAttacker
                };
                return (r.PlayerId, r.Name, r.AvatarId, r.Elo, agg);
            }).ToList();
        }
        else
        {
            var (ladder, aggregates) = await LoadLiveSeasonAggregatesAsync(db, season.Id);
            participants = ladder
                .Where(sp => sp.Player.IsActive && aggregates.ContainsKey(sp.PlayerId))
                .Select(sp => (sp.PlayerId, sp.Player.Name, sp.Player.AvatarId, sp.Elo, aggregates[sp.PlayerId]))
                .ToList();
        }

        var minGames = Constants.TimeThresholds.MinGamesForPositionBadge;

        // Goals per game (conceded asc / scored desc) → matches in position desc → seasonal ELO desc → PlayerId asc.
        var goalkeepers = participants
            .Where(p => p.Agg.GoalkeeperMatches >= minGames)
            .OrderBy(p => (double)p.Agg.GoalsConcededAsGoalkeeper / p.Agg.GoalkeeperMatches)
            .ThenByDescending(p => p.Agg.GoalkeeperMatches)
            .ThenByDescending(p => p.Elo)
            .ThenBy(p => p.PlayerId)
            .Select((p, index) => new PositionRanking
            {
                Rank = index + 1,
                PlayerId = p.PlayerId,
                PlayerName = p.Name,
                AvatarId = p.AvatarId,
                Games = p.Agg.GoalkeeperMatches,
                Goals = p.Agg.GoalsConcededAsGoalkeeper,
                AverageGoals = (double)p.Agg.GoalsConcededAsGoalkeeper / p.Agg.GoalkeeperMatches
            })
            .ToList();

        var attackers = participants
            .Where(p => p.Agg.AttackerMatches >= minGames)
            .OrderByDescending(p => (double)p.Agg.GoalsScoredAsAttacker / p.Agg.AttackerMatches)
            .ThenByDescending(p => p.Agg.AttackerMatches)
            .ThenByDescending(p => p.Elo)
            .ThenBy(p => p.PlayerId)
            .Select((p, index) => new PositionRanking
            {
                Rank = index + 1,
                PlayerId = p.PlayerId,
                PlayerName = p.Name,
                AvatarId = p.AvatarId,
                Games = p.Agg.AttackerMatches,
                Goals = p.Agg.GoalsScoredAsAttacker,
                AverageGoals = (double)p.Agg.GoalsScoredAsAttacker / p.Agg.AttackerMatches
            })
            .ToList();

        return (goalkeepers, attackers);
    }

    /// <summary>
    /// Season pair standings — wins by score, minimum games applied at render time, ordered with
    /// the pair-award tie-break chain (win rate desc → matches desc → combined seasonal ELO desc →
    /// smaller PlayerId asc). Frozen SeasonPair rows for closed seasons (pairs with a member
    /// inactive at close hidden; no average score stored), live aggregation for the active one.
    /// </summary>
    public async Task<List<PairStats>> GetSeasonPairRankingsAsync(Season season)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var ladder = await db.SeasonPlayers
            .AsNoTracking()
            .Include(sp => sp.Player)
            .Where(sp => sp.SeasonId == season.Id)
            .ToListAsync();
        var eloByPlayer = ladder.ToDictionary(sp => sp.PlayerId, sp => sp.Elo);
        var playerById = ladder.ToDictionary(sp => sp.PlayerId, sp => sp.Player);

        List<PairStats> pairs;
        if (season.IsClosed)
        {
            // Frozen view: what a closed season displays never depends on current IsActive — only
            // on the state frozen at close (FinalRank == null ⇔ inactive at close).
            var activeAtClose = (await db.SeasonPlayerResults
                    .Where(r => r.SeasonPlayer.SeasonId == season.Id && r.FinalRank != null)
                    .Select(r => r.SeasonPlayer.PlayerId)
                    .ToListAsync())
                .ToHashSet();

            pairs = (await db.SeasonPairs
                    .Where(p => p.SeasonId == season.Id)
                    .Include(p => p.Player1)
                    .Include(p => p.Player2)
                    .ToListAsync())
                .Where(p => activeAtClose.Contains(p.Player1Id) && activeAtClose.Contains(p.Player2Id))
                .Select(p => new PairStats
                {
                    Player1Id = p.Player1Id,
                    Player1Name = p.Player1.Name,
                    Player1AvatarId = p.Player1.AvatarId,
                    Player2Id = p.Player2Id,
                    Player2Name = p.Player2.Name,
                    Player2AvatarId = p.Player2.AvatarId,
                    Matches = p.MatchesTogether,
                    Wins = p.WinsTogether,
                    Losses = p.MatchesTogether - p.WinsTogether,
                    WinRate = p.MatchesTogether > 0 ? (double)p.WinsTogether / p.MatchesTogether * 100 : 0
                })
                .ToList();
        }
        else
        {
            var matches = await db.Matches
                .AsNoTracking()
                .Include(m => m.MatchPlayers)
                .Where(m => m.SeasonId == season.Id)
                .ToListAsync();

            pairs = SeasonAggregates.ComputePairs(matches)
                .Where(kv => playerById.TryGetValue(kv.Key.Player1Id, out var p1) && p1.IsActive &&
                             playerById.TryGetValue(kv.Key.Player2Id, out var p2) && p2.IsActive)
                .Select(kv => new PairStats
                {
                    Player1Id = kv.Key.Player1Id,
                    Player1Name = playerById[kv.Key.Player1Id].Name,
                    Player1AvatarId = playerById[kv.Key.Player1Id].AvatarId,
                    Player2Id = kv.Key.Player2Id,
                    Player2Name = playerById[kv.Key.Player2Id].Name,
                    Player2AvatarId = playerById[kv.Key.Player2Id].AvatarId,
                    Matches = kv.Value.Matches,
                    Wins = kv.Value.Wins,
                    Losses = kv.Value.Matches - kv.Value.Wins,
                    WinRate = kv.Value.Matches > 0 ? (double)kv.Value.Wins / kv.Value.Matches * 100 : 0,
                    TotalScore = kv.Value.TotalScore,
                    AverageScore = kv.Value.Matches > 0 ? (double)kv.Value.TotalScore / kv.Value.Matches : 0
                })
                .ToList();
        }

        return pairs
            .Where(p => p.Matches >= Constants.TimeThresholds.MinGamesForPartnerStats)
            .OrderByDescending(p => p.WinRate)
            .ThenByDescending(p => p.Matches)
            .ThenByDescending(p => eloByPlayer.GetValueOrDefault(p.Player1Id) + eloByPlayer.GetValueOrDefault(p.Player2Id))
            .ThenBy(p => Math.Min(p.Player1Id, p.Player2Id))
            .ToList();
    }

    private static async Task<(List<SeasonPlayer> Ladder, Dictionary<int, SeasonAggregates.ParticipantAggregate> Aggregates)>
        LoadLiveSeasonAggregatesAsync(AppDbContext db, int seasonId)
    {
        var matches = await db.Matches
            .AsNoTracking()
            .Include(m => m.MatchPlayers)
            .Where(m => m.SeasonId == seasonId)
            .OrderBy(m => m.PlayedAt).ThenBy(m => m.Id)
            .ToListAsync();

        var ladder = await db.SeasonPlayers
            .AsNoTracking()
            .Include(sp => sp.Player)
            .Where(sp => sp.SeasonId == seasonId)
            .ToListAsync();

        return (ladder, SeasonAggregates.ComputeParticipants(matches));
    }

    public async Task<(List<PositionRanking> Goalkeepers, List<PositionRanking> Attackers)> GetPositionRankingsAsync(int teamId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var players = await db.Players
            .Where(p => p.TeamId == teamId && p.IsActive)
            .ToListAsync();

        if (players.Count == 0)
            return ([], []);

        var playerIds = players.Select(p => p.Id).ToList();
        var allMatchPlayers = await db.MatchPlayers
            .Where(mp => playerIds.Contains(mp.PlayerId))
            .Include(mp => mp.Match)
            .ToListAsync();

        var matchPlayersByPlayer = allMatchPlayers
            .GroupBy(mp => mp.PlayerId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var goalkeepers = new List<PositionRanking>();
        var attackers = new List<PositionRanking>();
        var minGames = Constants.TimeThresholds.MinGamesForPositionBadge;

        foreach (var player in players)
        {
            var matchPlayers = matchPlayersByPlayer.GetValueOrDefault(player.Id) ?? [];
            var stats = CalculateStreaksAndPositionStats(matchPlayers);

            if (stats.GoalkeeperCount >= minGames)
            {
                goalkeepers.Add(new PositionRanking
                {
                    PlayerId = player.Id,
                    PlayerName = player.Name,
                    AvatarId = player.AvatarId,
                    Games = stats.GoalkeeperCount,
                    Goals = stats.GoalsConcededAsGk,
                    AverageGoals = (double)stats.GoalsConcededAsGk / stats.GoalkeeperCount
                });
            }

            if (stats.AttackerCount >= minGames)
            {
                attackers.Add(new PositionRanking
                {
                    PlayerId = player.Id,
                    PlayerName = player.Name,
                    AvatarId = player.AvatarId,
                    Games = stats.AttackerCount,
                    Goals = stats.GoalsScoredAsAtk,
                    AverageGoals = (double)stats.GoalsScoredAsAtk / stats.AttackerCount
                });
            }
        }

        // Goalkeepers ranked by lowest goals conceded per game.
        goalkeepers = goalkeepers.OrderBy(g => g.AverageGoals).ThenByDescending(g => g.Games).ToList();
        var rank = 1;
        foreach (var gk in goalkeepers) gk.Rank = rank++;

        // Attackers ranked by highest goals scored per game.
        attackers = attackers.OrderByDescending(a => a.AverageGoals).ThenByDescending(a => a.Games).ToList();
        rank = 1;
        foreach (var atk in attackers) atk.Rank = rank++;

        return (goalkeepers, attackers);
    }

    public async Task<List<PairStats>> GetPairRankingsAsync(int teamId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var matches = await db.Matches
            .Where(m => m.TeamId == teamId)
            .Include(m => m.MatchPlayers)
                .ThenInclude(mp => mp.Player)
            .ToListAsync();

        var pairStats = new Dictionary<string, PairStats>();

        foreach (var match in matches)
        {
            ProcessTeamPair(match, 1, pairStats);
            ProcessTeamPair(match, 2, pairStats);
        }

        return pairStats.Values
            .Where(p => p.Matches >= Constants.TimeThresholds.MinGamesForPartnerStats)
            .OrderByDescending(p => p.WinRate)
            .ThenByDescending(p => p.Matches)
            .ToList();
    }

    private static void ProcessTeamPair(Match match, int teamNumber, Dictionary<string, PairStats> pairStats)
    {
        var teamPlayers = match.MatchPlayers
            .Where(mp => mp.TeamNumber == teamNumber)
            .OrderBy(mp => mp.PlayerId)
            .ToList();

        if (teamPlayers.Count != 2) return;

        var key = $"{teamPlayers[0].PlayerId}-{teamPlayers[1].PlayerId}";
        var won = GetTeamScore(match, teamNumber) > GetOpponentScore(match, teamNumber);
        var teamScore = GetTeamScore(match, teamNumber);

        if (!pairStats.TryGetValue(key, out var pair))
        {
            pair = new PairStats
            {
                Player1Id = teamPlayers[0].PlayerId,
                Player1Name = teamPlayers[0].Player.Name,
                Player1AvatarId = teamPlayers[0].Player.AvatarId,
                Player2Id = teamPlayers[1].PlayerId,
                Player2Name = teamPlayers[1].Player.Name,
                Player2AvatarId = teamPlayers[1].Player.AvatarId
            };
            pairStats[key] = pair;
        }
        pair.Matches++;
        pair.TotalScore += teamScore;
        if (won) pair.Wins++;
        else pair.Losses++;
        pair.WinRate = (double)pair.Wins / pair.Matches * 100;
        pair.AverageScore = (double)pair.TotalScore / pair.Matches;
    }

    /// <summary>
    /// Helper method to get team score from a match
    /// </summary>
    private static int GetTeamScore(Match match, int teamNumber)
    {
        return teamNumber == 1 ? match.Team1Score : match.Team2Score;
    }

    /// <summary>
    /// Helper method to get opponent score from a match
    /// </summary>
    private static int GetOpponentScore(Match match, int teamNumber)
    {
        return teamNumber == 1 ? match.Team2Score : match.Team1Score;
    }

    /// <summary>
    /// Calculates streaks and position statistics from a list of match players
    /// </summary>
    private static StreakAndPositionResult CalculateStreaksAndPositionStats(List<MatchPlayer> matchPlayers)
    {
        var result = new StreakAndPositionResult();
        var tempStreak = 0;

        foreach (var mp in matchPlayers)
        {
            var teamScore = GetTeamScore(mp.Match, mp.TeamNumber);
            var opponentScore = GetOpponentScore(mp.Match, mp.TeamNumber);
            var won = teamScore > opponentScore;

            if (won)
            {
                result.Wins++;
                tempStreak = tempStreak > 0 ? tempStreak + 1 : 1;
                result.LongestWinStreak = Math.Max(result.LongestWinStreak, tempStreak);

                // Check for table sender (won 10-0)
                if (teamScore == 10 && opponentScore == 0)
                    result.TableSenderCount++;

                // Destroyer: won by a 7+ goal margin
                if (teamScore - opponentScore >= 7)
                    result.DestroyerWinCount++;
            }
            else
            {
                result.Losses++;
                tempStreak = tempStreak < 0 ? tempStreak - 1 : -1;
                result.LongestLossStreak = Math.Max(result.LongestLossStreak, -tempStreak);

                // Check for under the table (lost with 0 score)
                if (teamScore == 0)
                    result.UnderTableCount++;

                // Lucker: lost with own team scoring exactly 1 goal
                if (teamScore == 1)
                    result.LuckyLossCount++;
            }
            result.CurrentStreak = tempStreak;

            // Position tracking - track team performance when playing each position
            if (mp.Position == Constants.Positions.Goalkeeper)
            {
                result.GoalkeeperCount++;
                result.GoalsScoredAsGk += teamScore;
                result.GoalsConcededAsGk += opponentScore;
                if (won) result.WinsAsGk++;
            }
            else
            {
                result.AttackerCount++;
                result.GoalsScoredAsAtk += teamScore;
                result.GoalsConcededAsAtk += opponentScore;
                if (won) result.WinsAsAtk++;
            }
        }

        return result;
    }

    /// <summary>
    /// Result of streak and position calculation
    /// </summary>
    private class StreakAndPositionResult
    {
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int CurrentStreak { get; set; }
        public int LongestWinStreak { get; set; }
        public int LongestLossStreak { get; set; }
        public int UnderTableCount { get; set; }
        public int TableSenderCount { get; set; }
        public int LuckyLossCount { get; set; }
        public int DestroyerWinCount { get; set; }
        public int GoalkeeperCount { get; set; }
        public int AttackerCount { get; set; }
        public int GoalsScoredAsGk { get; set; }
        public int GoalsConcededAsGk { get; set; }
        public int GoalsScoredAsAtk { get; set; }
        public int GoalsConcededAsAtk { get; set; }
        public int WinsAsGk { get; set; }
        public int WinsAsAtk { get; set; }
    }
}

public class PlayerStats
{
    public int CurrentElo { get; set; } = Constants.Elo.DefaultRating;
    public int HighestElo { get; set; } = Constants.Elo.DefaultRating;
    public int LowestElo { get; set; } = Constants.Elo.DefaultRating;
    public int TotalMatches { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public double WinRate { get; set; }
    public int CurrentStreak { get; set; }
    public int LongestWinStreak { get; set; }
    public int LongestLossStreak { get; set; }
    public double AvgEloChangeOnWin { get; set; }
    public double AvgEloChangeOnLoss { get; set; }
    public int AvgOpponentElo { get; set; }
    public int AvgTeammateElo { get; set; }
    public List<bool> RecentForm { get; set; } = [];
    public double RecentFormWinRate { get; set; }
    public int CarriedCount { get; set; }
    public int CarryCount { get; set; }
    public double TeammateVariety { get; set; }
    public int UniqueTeammates { get; set; }
    public int ActiveRosterPartners { get; set; }
    public bool HasEnoughGamesForVariety { get; set; }
    public string PreferredPosition { get; set; } = "Flexible";
    public string BetterPosition { get; set; } = "-";
    public int GamesAsGk { get; set; }
    public int GamesAsAtk { get; set; }
    public int WinsAsGk { get; set; }
    public int WinsAsAtk { get; set; }
    public double WinRateAsGk { get; set; }
    public double WinRateAsAtk { get; set; }
    public int GoalsScoredAsGk { get; set; }
    public int GoalsConcededAsGk { get; set; }
    public int GoalsScoredAsAtk { get; set; }
    public int GoalsConcededAsAtk { get; set; }
    public string? BestPartner { get; set; }
    public double BestPartnerWinRate { get; set; }
    public int BestPartnerGames { get; set; }
    public string? WorstPartner { get; set; }
    public double WorstPartnerWinRate { get; set; }
    public int WorstPartnerGames { get; set; }
    public string? EasiestEnemy { get; set; }
    public double EasiestEnemyWinRate { get; set; }
    public int EasiestEnemyGames { get; set; }
    public string? HardestEnemy { get; set; }
    public double HardestEnemyWinRate { get; set; }
    public int HardestEnemyGames { get; set; }
    public int UnderTableCount { get; set; }
    public int TableSenderCount { get; set; }
    public List<EloHistoryPoint> EloHistory { get; set; } = [];

    // ELO expectation vs. reality
    public double ExpectedWins { get; set; }
    public double WinsVsExpected => Wins - ExpectedWins;

    // Goal margins
    public double AvgWinMargin { get; set; }
    public double AvgLossMargin { get; set; }

    // Performance bucketed by opponent strength (avg opp ELO vs own pre-match ELO)
    public int GamesVsStronger { get; set; }
    public int WinsVsStronger { get; set; }
    public double WinRateVsStronger { get; set; }
    public int GamesVsWeaker { get; set; }
    public int WinsVsWeaker { get; set; }
    public double WinRateVsWeaker { get; set; }

    // Clean sheets (matches as GK with 0 conceded)
    public int CleanSheetsAsGk { get; set; }

    // Full relationship lists
    public List<RelationshipStat> Partners { get; set; } = [];
    public List<RelationshipStat> Enemies { get; set; } = [];

    // Milestones
    public DateTimeOffset? FirstMatchDate { get; set; }
    public DateTimeOffset? PeakEloDate { get; set; }
    public int BiggestEloGain { get; set; }
    public DateTimeOffset? BiggestEloGainDate { get; set; }
    public int BiggestEloLoss { get; set; }
    public DateTimeOffset? BiggestEloLossDate { get; set; }

    // Activity
    public List<ActivityMonth> MatchesByMonth { get; set; } = [];
    public List<DayOfWeekStat> MatchesByDayOfWeek { get; set; } = [];
}

public sealed record ActivityMonth(int Year, int Month, int Games);

public sealed record DayOfWeekStat(DayOfWeek Day, int Games, int Wins)
{
    public double WinRate => Games > 0 ? (double)Wins / Games * 100 : 0;
    public int Losses => Games - Wins;
}

public class RelationshipStat
{
    public int PlayerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int AvatarId { get; set; }
    public int Games { get; set; }
    public int Wins { get; set; }
    public double WinRate { get; set; }
    public double AvgEloChange { get; set; }
    public int Losses => Games - Wins;
}

public class EloHistoryPoint
{
    public DateTimeOffset Date { get; set; }
    public int Elo { get; set; }
}

public class PlayerRanking
{
    public int Rank { get; set; }
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public int AvatarId { get; set; }
    public int Elo { get; set; }
    public int Matches { get; set; }
    public int Wins { get; set; }
    public double WinRate { get; set; }
}

/// <summary>One row of season standings — live (active season) or frozen (closed season).</summary>
public class SeasonStandingRow
{
    public int Rank { get; set; }
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public int AvatarId { get; set; }
    /// <summary>Seasonal ELO (final for closed seasons).</summary>
    public int Elo { get; set; }
    public int Matches { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public double WinRate { get; set; }
    public int LongestWinStreak { get; set; }
    public int LongestLossStreak { get; set; }
}

public class PositionRanking
{
    public int Rank { get; set; }
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public int AvatarId { get; set; }
    public int Games { get; set; }
    public int Goals { get; set; }
    public double AverageGoals { get; set; }
}

public class PairStats
{
    public int Player1Id { get; set; }
    public string Player1Name { get; set; } = string.Empty;
    public int Player1AvatarId { get; set; }
    public int Player2Id { get; set; }
    public string Player2Name { get; set; } = string.Empty;
    public int Player2AvatarId { get; set; }
    public int Matches { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public double WinRate { get; set; }
    public int TotalScore { get; set; }
    public double AverageScore { get; set; }
}

