using Fotbalek.Web.Data;
using Fotbalek.Web.Data.Entities;
using Fotbalek.Web.Services.Stats.Activity;
using Fotbalek.Web.Services.Stats.Core;
using Fotbalek.Web.Services.Stats.Special;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Web.Services;

public class StatsService(AppDbContext db)
{
    public async Task<PlayerStats> GetPlayerStatsAsync(int playerId)
    {
        var matchPlayers = await db.MatchPlayers
            .Where(mp => mp.PlayerId == playerId)
            .Include(mp => mp.Match)
                .ThenInclude(m => m.MatchPlayers)
                    .ThenInclude(mp => mp.Player)
            .OrderBy(mp => mp.Match.PlayedAt)
            .ToListAsync();

        var player = await db.Players.FindAsync(playerId);
        if (player == null)
            return new PlayerStats();

        var stats = new PlayerStats
        {
            CurrentElo = player.Elo,
            TotalMatches = matchPlayers.Count
        };

        if (matchPlayers.Count == 0)
            return stats;

        // ELO history - consider all ELO values including current
        var allEloValues = matchPlayers.Select(mp => mp.EloBefore)
            .Concat(matchPlayers.Select(mp => mp.EloAfter))
            .Append(player.Elo);
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
                .Select(p => new { Partner = p, OwnEloChange = mp.EloChange }))
            .GroupBy(x => x.Partner.PlayerId)
            .Select(g => new RelationshipStat
            {
                PlayerId = g.Key,
                Name = g.First().Partner.Player.Name,
                AvatarId = g.First().Partner.Player.AvatarId,
                Games = g.Count(),
                Wins = g.Count(x => x.OwnEloChange > 0),
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

        // Enemy stats (full list; win counted from player's perspective when enemy lost ELO)
        var enemyStats = matchPlayers
            .SelectMany(mp => mp.Match.MatchPlayers
                .Where(p => p.TeamNumber != mp.TeamNumber)
                .Select(p => new { Enemy = p, OwnEloChange = mp.EloChange }))
            .GroupBy(x => x.Enemy.PlayerId)
            .Select(g => new RelationshipStat
            {
                PlayerId = g.Key,
                Name = g.First().Enemy.Player.Name,
                AvatarId = g.First().Enemy.Player.AvatarId,
                Games = g.Count(),
                Wins = g.Count(x => x.OwnEloChange > 0),
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

        // Average ELO change on wins / losses
        var winningMps = matchPlayers.Where(mp => mp.EloChange > 0).ToList();
        var losingMps = matchPlayers.Where(mp => mp.EloChange < 0).ToList();
        stats.AvgEloChangeOnWin = winningMps.Count > 0 ? winningMps.Average(mp => mp.EloChange) : 0;
        stats.AvgEloChangeOnLoss = losingMps.Count > 0 ? losingMps.Average(mp => mp.EloChange) : 0;

        // Average opponent / teammate ELO (using pre-match ELO at the time of each match)
        var opponentElos = matchPlayers
            .SelectMany(mp => mp.Match.MatchPlayers
                .Where(p => p.TeamNumber != mp.TeamNumber)
                .Select(p => p.EloBefore))
            .ToList();
        stats.AvgOpponentElo = opponentElos.Count > 0 ? (int)Math.Round(opponentElos.Average()) : 0;

        var teammateElos = matchPlayers
            .SelectMany(mp => mp.Match.MatchPlayers
                .Where(p => p.TeamNumber == mp.TeamNumber && p.PlayerId != playerId)
                .Select(p => p.EloBefore))
            .ToList();
        stats.AvgTeammateElo = teammateElos.Count > 0 ? (int)Math.Round(teammateElos.Average()) : 0;

        // Recent form: last 10 matches (matchPlayers is ordered ascending by PlayedAt)
        var recent = matchPlayers.TakeLast(10).ToList();
        stats.RecentForm = recent.Select(mp => mp.EloChange > 0).ToList();
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
            var carry = CarriedStat.AnalyzeCarry(winners[0], winners[1], losers[0], losers[1]);
            if (carry is null) continue;
            if (carry.Value.CarriedId == playerId) stats.CarriedCount++;
            if (carry.Value.CarrierId == playerId) stats.CarryCount++;
        }

        // ELO history for chart
        stats.EloHistory = matchPlayers
            .Select(mp => new EloHistoryPoint
            {
                Date = mp.Match.PlayedAt,
                Elo = mp.EloAfter
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
            var teamAvg = teammate != null ? (mp.EloBefore + teammate.EloBefore) / 2.0 : mp.EloBefore;
            var oppAvg = opp.Average(p => (double)p.EloBefore);
            var expected = 1.0 / (1.0 + Math.Pow(10, (oppAvg - teamAvg) / 400.0));
            expectedWins += expected;

            var won = mp.EloChange > 0;
            if (oppAvg - mp.EloBefore >= StrongerThreshold)
            {
                gamesVsStronger++;
                if (won) winsVsStronger++;
            }
            else if (mp.EloBefore - oppAvg >= StrongerThreshold)
            {
                gamesVsWeaker++;
                if (won) winsVsWeaker++;
            }

            var ownScore = mp.TeamNumber == 1 ? mp.Match.Team1Score : mp.Match.Team2Score;
            var oppScore = mp.TeamNumber == 1 ? mp.Match.Team2Score : mp.Match.Team1Score;
            var margin = ownScore - oppScore;
            if (won) { winMarginSum += margin; winMarginCount++; }
            else if (mp.EloChange < 0) { lossMarginSum += -margin; lossMarginCount++; }

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
            var biggestGain = matchPlayers.OrderByDescending(mp => mp.EloChange).First();
            stats.BiggestEloGain = biggestGain.EloChange;
            stats.BiggestEloGainDate = biggestGain.Match.PlayedAt;

            var biggestLoss = matchPlayers.OrderBy(mp => mp.EloChange).First();
            stats.BiggestEloLoss = biggestLoss.EloChange;
            stats.BiggestEloLossDate = biggestLoss.Match.PlayedAt;

            var peak = matchPlayers.OrderByDescending(mp => mp.EloAfter).First();
            stats.PeakEloDate = peak.Match.PlayedAt;
        }

        return stats;
    }

    public async Task<List<PlayerRanking>> GetRankingsAsync(int teamId)
    {
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

    public async Task<(List<PositionRanking> Goalkeepers, List<PositionRanking> Attackers)> GetPositionRankingsAsync(int teamId)
    {
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
        var won = teamPlayers[0].EloChange > 0;
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
            var won = mp.EloChange > 0;
            var teamScore = GetTeamScore(mp.Match, mp.TeamNumber);
            var opponentScore = GetOpponentScore(mp.Match, mp.TeamNumber);

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

