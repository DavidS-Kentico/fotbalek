using Fotbalek.Application.Features.Stats.Core;
using Fotbalek.Domain.Entities;
using Fotbalek.SharedKernel;

namespace Fotbalek.Application.Features.Stats.Queries;

/// <summary>Shared aggregate math used by the stats queries (ported from the old StatsService).</summary>
internal static class StatsCalculations
{
    /// <summary>
    /// Calculates streaks and position statistics from a list of match players
    /// (expected in chronological order).
    /// </summary>
    public static StreakAndPositionResult CalculateStreaksAndPositionStats(List<MatchPlayer> matchPlayers)
    {
        var result = new StreakAndPositionResult();
        var tempStreak = 0;

        foreach (var mp in matchPlayers)
        {
            var teamScore = mp.Match.TeamScore(mp.TeamNumber);
            var opponentScore = mp.Match.OpponentScore(mp.TeamNumber);
            var won = teamScore > opponentScore;

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
                result.LongestLossStreak = Math.Max(result.LongestLossStreak, -tempStreak);

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

    public sealed class StreakAndPositionResult
    {
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int CurrentStreak { get; set; }
        public int LongestWinStreak { get; set; }
        public int LongestLossStreak { get; set; }
        public int UnderTableCount { get; set; }
        public int TableSenderCount { get; set; }
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
