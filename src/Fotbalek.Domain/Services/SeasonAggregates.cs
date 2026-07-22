using Fotbalek.Domain.Entities;
using Fotbalek.SharedKernel;

namespace Fotbalek.Domain.Services;

/// <summary>
/// Score-based per-player and per-pair aggregates over a season's matches. Shared by the close
/// procedure (season handlers) and the live season views (stats queries), so frozen results and
/// live standings agree by construction.
/// </summary>
public static class SeasonAggregates
{
    public sealed class ParticipantAggregate
    {
        public int Wins, Losses, MatchesPlayed;
        public int LongestWinStreak, LongestLossStreak;
        public int GoalkeeperMatches, GoalsConcededAsGoalkeeper, AttackerMatches, GoalsScoredAsAttacker;
        private int _currentWin, _currentLoss;

        public void Record(bool won, string position, int teamScore, int opponentScore)
        {
            MatchesPlayed++;
            if (won)
            {
                Wins++;
                _currentWin++;
                _currentLoss = 0;
                LongestWinStreak = Math.Max(LongestWinStreak, _currentWin);
            }
            else
            {
                Losses++;
                _currentLoss++;
                _currentWin = 0;
                LongestLossStreak = Math.Max(LongestLossStreak, _currentLoss);
            }

            if (position == Constants.Positions.Goalkeeper)
            {
                GoalkeeperMatches++;
                GoalsConcededAsGoalkeeper += opponentScore;
            }
            else
            {
                AttackerMatches++;
                GoalsScoredAsAttacker += teamScore;
            }
        }
    }

    /// <summary>Wins by score; streaks over the matches in the given (chronological) order.</summary>
    public static Dictionary<int, ParticipantAggregate> ComputeParticipants(IEnumerable<Match> chronologicalMatches)
    {
        var result = new Dictionary<int, ParticipantAggregate>();
        foreach (var match in chronologicalMatches)
        {
            foreach (var mp in match.MatchPlayers)
            {
                if (!result.TryGetValue(mp.PlayerId, out var agg))
                {
                    agg = new ParticipantAggregate();
                    result[mp.PlayerId] = agg;
                }
                var teamScore = mp.TeamNumber == 1 ? match.Team1Score : match.Team2Score;
                var opponentScore = mp.TeamNumber == 1 ? match.Team2Score : match.Team1Score;
                agg.Record(teamScore > opponentScore, mp.Position, teamScore, opponentScore);
            }
        }
        return result;
    }

    /// <summary>One entry per duo (Player1Id &lt; Player2Id) that played at least one match together; wins by score.</summary>
    public static Dictionary<(int Player1Id, int Player2Id), (int Matches, int Wins, int TotalScore)> ComputePairs(IEnumerable<Match> matches)
    {
        var result = new Dictionary<(int, int), (int Matches, int Wins, int TotalScore)>();
        foreach (var match in matches)
        {
            foreach (var teamNumber in new[] { 1, 2 })
            {
                var team = match.MatchPlayers
                    .Where(mp => mp.TeamNumber == teamNumber)
                    .OrderBy(mp => mp.PlayerId)
                    .ToList();
                if (team.Count != 2) continue;

                var key = (team[0].PlayerId, team[1].PlayerId);
                var teamScore = teamNumber == 1 ? match.Team1Score : match.Team2Score;
                var won = teamNumber == 1
                    ? match.Team1Score > match.Team2Score
                    : match.Team2Score > match.Team1Score;
                result.TryGetValue(key, out var current);
                result[key] = (current.Matches + 1, current.Wins + (won ? 1 : 0), current.TotalScore + teamScore);
            }
        }
        return result;
    }
}
