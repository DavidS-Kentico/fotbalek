using Fotbalek.Web.Data.Entities;
using Fotbalek.Web.Services.Stats.Core;

namespace Fotbalek.Web.Services.Stats.Streaks;

internal record PlayerStreaks(int CurrentWinStreak, int CurrentLossStreak, int LongestWinStreak, int LongestLossStreak);

internal static class StreakComputer
{
    public static Dictionary<int, PlayerStreaks> Compute(StatContext context)
    {
        var perPlayer = context.Matches
            .OrderBy(m => m.PlayedAt)
            .ThenBy(m => m.Id)
            .SelectMany(m => m.MatchPlayers.Select(mp => (mp.PlayerId, IsWin: mp.IsWinner())))
            .GroupBy(x => x.PlayerId);

        var result = new Dictionary<int, PlayerStreaks>();
        foreach (var group in perPlayer)
        {
            int currentWin = 0, currentLoss = 0, longestWin = 0, longestLoss = 0;
            foreach (var (_, isWin) in group)
            {
                if (isWin)
                {
                    currentWin++;
                    currentLoss = 0;
                    if (currentWin > longestWin) longestWin = currentWin;
                }
                else
                {
                    currentLoss++;
                    currentWin = 0;
                    if (currentLoss > longestLoss) longestLoss = currentLoss;
                }
            }
            result[group.Key] = new PlayerStreaks(currentWin, currentLoss, longestWin, longestLoss);
        }
        return result;
    }
}
