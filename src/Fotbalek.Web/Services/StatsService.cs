using Fotbalek.Web.Data;
using Fotbalek.Web.Data.Entities;
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
        stats.UnderTableCount = streakResult.UnderTableCount;
        stats.GamesAsGk = streakResult.GoalkeeperCount;
        stats.GamesAsAtk = streakResult.AttackerCount;
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

        // Partner stats (min 3 games together)
        var partnerStats = matchPlayers
            .SelectMany(mp => mp.Match.MatchPlayers
                .Where(p => p.TeamNumber == mp.TeamNumber && p.PlayerId != playerId))
            .GroupBy(p => p.PlayerId)
            .Select(g => new
            {
                PartnerId = g.Key,
                PartnerName = g.First().Player.Name,
                Games = g.Count(),
                Wins = g.Count(p => p.EloChange > 0)
            })
            .Where(p => p.Games >= Constants.TimeThresholds.MinGamesForPartnerStats)
            .ToList();

        if (partnerStats.Count != 0)
        {
            var best = partnerStats.OrderByDescending(p => (double)p.Wins / p.Games).First();
            stats.BestPartner = best.PartnerName;
            stats.BestPartnerWinRate = (double)best.Wins / best.Games * 100;
            stats.BestPartnerGames = best.Games;

            // Only show worst partner if there's more than one partner (to avoid same as best)
            if (partnerStats.Count > 1)
            {
                var worst = partnerStats.OrderBy(p => (double)p.Wins / p.Games).First();
                stats.WorstPartner = worst.PartnerName;
                stats.WorstPartnerWinRate = (double)worst.Wins / worst.Games * 100;
                stats.WorstPartnerGames = worst.Games;
            }
        }

        // Enemy stats (min 3 games against)
        var enemyStats = matchPlayers
            .SelectMany(mp => mp.Match.MatchPlayers
                .Where(p => p.TeamNumber != mp.TeamNumber)) // Opponents are on different team
            .GroupBy(p => p.PlayerId)
            .Select(g => new
            {
                EnemyId = g.Key,
                EnemyName = g.First().Player.Name,
                Games = g.Count(),
                // Count wins from player's perspective: enemy lost means player won
                Wins = g.Count(p => p.EloChange < 0)
            })
            .Where(e => e.Games >= Constants.TimeThresholds.MinGamesForPartnerStats)
            .ToList();

        if (enemyStats.Count != 0)
        {
            // Easiest enemy = highest win rate against them
            var easiest = enemyStats.OrderByDescending(e => (double)e.Wins / e.Games).First();
            stats.EasiestEnemy = easiest.EnemyName;
            stats.EasiestEnemyWinRate = (double)easiest.Wins / easiest.Games * 100;
            stats.EasiestEnemyGames = easiest.Games;

            // Only show hardest enemy if there's more than one enemy (to avoid same as easiest)
            if (enemyStats.Count > 1)
            {
                // Hardest enemy = lowest win rate against them
                var hardest = enemyStats.OrderBy(e => (double)e.Wins / e.Games).First();
                stats.HardestEnemy = hardest.EnemyName;
                stats.HardestEnemyWinRate = (double)hardest.Wins / hardest.Games * 100;
                stats.HardestEnemyGames = hardest.Games;
            }
        }

        // ELO history for chart
        stats.EloHistory = matchPlayers
            .Select(mp => new EloHistoryPoint
            {
                Date = mp.Match.PlayedAt,
                Elo = mp.EloAfter
            })
            .ToList();

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

    public async Task<TeamBadges> GetBadgesAsync(int teamId)
    {
        var badges = new TeamBadges();
        var players = await db.Players
            .Where(p => p.TeamId == teamId && p.IsActive)
            .ToListAsync();

        if (players.Count == 0) return badges;

        // Top Rated - highest ELO
        var topRated = players.MaxBy(p => p.Elo);
        if (topRated != null)
        {
            badges.TopRated = new BadgeHolder { PlayerId = topRated.Id, PlayerName = topRated.Name, Value = topRated.Elo };
        }

        // Last Place - lowest ELO
        var lastPlace = players.MinBy(p => p.Elo);
        if (lastPlace != null)
        {
            badges.LastPlace = new BadgeHolder { PlayerId = lastPlace.Id, PlayerName = lastPlace.Name, Value = lastPlace.Elo };
        }

        // Fix N+1: Load all match players for all players in a single query
        var playerIds = players.Select(p => p.Id).ToList();
        var allMatchPlayers = await db.MatchPlayers
            .Where(mp => playerIds.Contains(mp.PlayerId))
            .Include(mp => mp.Match)
            .OrderBy(mp => mp.Match.PlayedAt)
            .ToListAsync();

        // Group by player for processing
        var matchPlayersByPlayer = allMatchPlayers
            .GroupBy(mp => mp.PlayerId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Calculate streaks, under table counts, and position stats for all players
        var playerStreaks = new Dictionary<int, (int current, int longest, int underTable, int tableSender, int wins, int totalGames)>();
        var positionStats = new Dictionary<int, (int gkGames, int gkTeamScored, int gkTeamConceded, int atkGames, int atkTeamScored, int atkTeamConceded)>();

        foreach (var player in players)
        {
            var matchPlayers = matchPlayersByPlayer.GetValueOrDefault(player.Id) ?? [];

            // Calculate streaks using helper
            var streakResult = CalculateStreaksAndPositionStats(matchPlayers);
            playerStreaks[player.Id] = (
                streakResult.CurrentStreak,
                streakResult.LongestWinStreak,
                streakResult.UnderTableCount,
                streakResult.TableSenderCount,
                streakResult.Wins,
                streakResult.Wins + streakResult.Losses
            );
            positionStats[player.Id] = (
                streakResult.GoalkeeperCount,
                streakResult.GoalsScoredAsGk,
                streakResult.GoalsConcededAsGk,
                streakResult.AttackerCount,
                streakResult.GoalsScoredAsAtk,
                streakResult.GoalsConcededAsAtk
            );
        }

        // Hot Streak - currently longest active win streak (min 3 wins)
        var hotStreak = playerStreaks
            .Where(ps => ps.Value.current >= Constants.TimeThresholds.MinGamesForPartnerStats)
            .OrderByDescending(ps => ps.Value.current)
            .FirstOrDefault();

        if (hotStreak.Value.current >= Constants.TimeThresholds.MinGamesForPartnerStats)
        {
            var player = players.First(p => p.Id == hotStreak.Key);
            badges.HotStreak = new BadgeHolder
            {
                PlayerId = player.Id,
                PlayerName = player.Name,
                Value = hotStreak.Value.current
            };
        }

        // Streak King - longest win streak in history
        var streakKing = playerStreaks.OrderByDescending(ps => ps.Value.longest).FirstOrDefault();
        if (streakKing.Value.longest > 0)
        {
            var player = players.First(p => p.Id == streakKing.Key);
            badges.StreakKing = new BadgeHolder
            {
                PlayerId = player.Id,
                PlayerName = player.Name,
                Value = streakKing.Value.longest
            };
        }

        // Table Diver - most under the table losses
        var tableDiver = playerStreaks.OrderByDescending(ps => ps.Value.underTable).FirstOrDefault();
        if (tableDiver.Value.underTable > 0)
        {
            var player = players.First(p => p.Id == tableDiver.Key);
            badges.TableDiver = new BadgeHolder
            {
                PlayerId = player.Id,
                PlayerName = player.Name,
                Value = tableDiver.Value.underTable
            };
        }

        // Table Sender - most 10-0 wins (sent enemies under the table)
        var tableSender = playerStreaks.OrderByDescending(ps => ps.Value.tableSender).FirstOrDefault();
        if (tableSender.Value.tableSender > 0)
        {
            var player = players.First(p => p.Id == tableSender.Key);
            badges.TableSender = new BadgeHolder
            {
                PlayerId = player.Id,
                PlayerName = player.Name,
                Value = tableSender.Value.tableSender
            };
        }

        // Best Win Rate - highest win rate with minimum 5 games
        var bestWinRate = playerStreaks
            .Where(ps => ps.Value.totalGames >= Constants.TimeThresholds.MinGamesForPositionBadge)
            .OrderByDescending(ps => (double)ps.Value.wins / ps.Value.totalGames)
            .FirstOrDefault();

        if (bestWinRate.Value.totalGames >= Constants.TimeThresholds.MinGamesForPositionBadge)
        {
            var player = players.First(p => p.Id == bestWinRate.Key);
            var winRate = (double)bestWinRate.Value.wins / bestWinRate.Value.totalGames * 100;
            badges.BestWinRate = new BadgeHolder
            {
                PlayerId = player.Id,
                PlayerName = player.Name,
                Value = (int)winRate // Store win rate as whole percentage
            };
        }

        // Tomko Memorial - most games played in a single day
        var gamesPerPlayerPerDay = allMatchPlayers
            .GroupBy(mp => new { mp.PlayerId, Date = mp.Match.PlayedAt.Date })
            .Select(g => new { g.Key.PlayerId, g.Key.Date, GamesCount = g.Count() })
            .ToList();

        var tomkoCandidate = gamesPerPlayerPerDay
            .OrderByDescending(x => x.GamesCount)
            .FirstOrDefault();

        if (tomkoCandidate != null && tomkoCandidate.GamesCount > 0)
        {
            var player = players.FirstOrDefault(p => p.Id == tomkoCandidate.PlayerId);
            if (player != null)
            {
                badges.TomkoMemorial = new BadgeHolder
                {
                    PlayerId = player.Id,
                    PlayerName = player.Name,
                    Value = tomkoCandidate.GamesCount
                };
            }
        }

        // Newcomers - joined in last 7 days
        var sevenDaysAgo = DateTimeOffset.UtcNow.AddDays(-Constants.TimeThresholds.RecentActivityDays);
        badges.Newcomers = players
            .Where(p => p.CreatedAt >= sevenDaysAgo)
            .Select(p => new BadgeHolder { PlayerId = p.Id, PlayerName = p.Name })
            .ToList();

        // Best Goalkeeper - lowest goals conceded per game when playing as GK (min 5 games as GK)
        var bestGk = positionStats
            .Where(ps => ps.Value.gkGames >= Constants.TimeThresholds.MinGamesForPositionBadge)
            .OrderBy(ps => (double)ps.Value.gkTeamConceded / ps.Value.gkGames)
            .FirstOrDefault();

        if (bestGk.Value.gkGames >= Constants.TimeThresholds.MinGamesForPositionBadge)
        {
            var player = players.First(p => p.Id == bestGk.Key);
            var avgConceded = (double)bestGk.Value.gkTeamConceded / bestGk.Value.gkGames;
            badges.BestGoalkeeper = new BadgeHolder
            {
                PlayerId = player.Id,
                PlayerName = player.Name,
                Value = (int)(avgConceded * 10) // Store as tenths to keep precision
            };
        }

        // Best Attacker - highest goals scored per game when playing as ATK (min 5 games as ATK)
        var bestAtk = positionStats
            .Where(ps => ps.Value.atkGames >= Constants.TimeThresholds.MinGamesForPositionBadge)
            .OrderByDescending(ps => (double)ps.Value.atkTeamScored / ps.Value.atkGames)
            .FirstOrDefault();

        if (bestAtk.Value.atkGames >= Constants.TimeThresholds.MinGamesForPositionBadge)
        {
            var player = players.First(p => p.Id == bestAtk.Key);
            var avgScored = (double)bestAtk.Value.atkTeamScored / bestAtk.Value.atkGames;
            badges.BestAttacker = new BadgeHolder
            {
                PlayerId = player.Id,
                PlayerName = player.Name,
                Value = (int)(avgScored * 10) // Store as tenths to keep precision
            };
        }

        return badges;
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
            }
            else
            {
                result.Losses++;
                tempStreak = tempStreak < 0 ? tempStreak - 1 : -1;

                // Check for under the table (lost with 0 score)
                if (teamScore == 0)
                    result.UnderTableCount++;
            }
            result.CurrentStreak = tempStreak;

            // Position tracking - track team performance when playing each position
            if (mp.Position == Constants.Positions.Goalkeeper)
            {
                result.GoalkeeperCount++;
                result.GoalsScoredAsGk += teamScore;
                result.GoalsConcededAsGk += opponentScore;
            }
            else
            {
                result.AttackerCount++;
                result.GoalsScoredAsAtk += teamScore;
                result.GoalsConcededAsAtk += opponentScore;
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
        public int UnderTableCount { get; set; }
        public int TableSenderCount { get; set; }
        public int GoalkeeperCount { get; set; }
        public int AttackerCount { get; set; }
        public int GoalsScoredAsGk { get; set; }
        public int GoalsConcededAsGk { get; set; }
        public int GoalsScoredAsAtk { get; set; }
        public int GoalsConcededAsAtk { get; set; }
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
    public string PreferredPosition { get; set; } = "Flexible";
    public int GamesAsGk { get; set; }
    public int GamesAsAtk { get; set; }
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
    public List<EloHistoryPoint> EloHistory { get; set; } = [];
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

public class TeamBadges
{
    public BadgeHolder? HotStreak { get; set; }
    public BadgeHolder? StreakKing { get; set; }
    public BadgeHolder? LastPlace { get; set; }
    public BadgeHolder? TableDiver { get; set; }
    public BadgeHolder? TableSender { get; set; }
    public BadgeHolder? TopRated { get; set; }
    public BadgeHolder? BestGoalkeeper { get; set; }
    public BadgeHolder? BestAttacker { get; set; }
    public BadgeHolder? BestWinRate { get; set; }
    public BadgeHolder? TomkoMemorial { get; set; }
    public List<BadgeHolder> Newcomers { get; set; } = [];
}

public class BadgeHolder
{
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public int Value { get; set; }
}
