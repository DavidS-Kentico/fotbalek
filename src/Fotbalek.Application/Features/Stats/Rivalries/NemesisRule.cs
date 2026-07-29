using Fotbalek.Domain.Entities;
using Fotbalek.SharedKernel;

namespace Fotbalek.Application.Features.Stats.Rivalries;

/// <summary>
/// "Who beats me most" — the per-player half of the nemesis rule, extracted from
/// <see cref="NemesisStat"/> so the stats page and the <c>NemesisBeaten</c> notification cannot
/// disagree about who your nemesis is (AI/notifications.md §6.5).
/// <para>
/// The extraction added the tie-break the stat lacked: it used to take <c>.First()</c> after
/// ordering by ratio, so a player with two equally dominant opponents got an arbitrary one —
/// harmless in a leaderboard, but here it decides whether a notification fires at all.
/// </para>
/// </summary>
internal static class NemesisRule
{
    /// <summary>One head-to-head record from the perspective of the player being beaten.</summary>
    public readonly record struct Rivalry(int OpponentPlayerId, int Losses, int Games)
    {
        public double LossRatio => (double)Losses / Games;
    }

    /// <summary>
    /// The single worst opponent per player: the highest loss ratio over at least
    /// <c>Constants.Stats.MinHeadToHeadGames</c> head-to-head games, and only when that ratio
    /// exceeds one half. Ties break on more head-to-head games, then the lower PlayerId.
    /// Players with no qualifying rival are absent from the result.
    /// </summary>
    public static Dictionary<int, Rivalry> WorstPerPlayer(IEnumerable<Match> matches)
    {
        // (player, opponent) → (losses by player, games between them)
        var pairs = new Dictionary<(int Self, int Opp), (int Losses, int Games)>();

        foreach (var match in matches)
        {
            var team1 = match.MatchPlayers.Where(mp => mp.TeamNumber == 1).ToList();
            var team2 = match.MatchPlayers.Where(mp => mp.TeamNumber == 2).ToList();
            if (team1.Count == 0 || team2.Count == 0) continue;
            var team1Won = match.Team1Score > match.Team2Score;

            foreach (var p1 in team1)
            {
                foreach (var p2 in team2)
                {
                    Bump(pairs, p1.PlayerId, p2.PlayerId, selfLost: !team1Won);
                    Bump(pairs, p2.PlayerId, p1.PlayerId, selfLost: team1Won);
                }
            }
        }

        return pairs
            .Where(kv => kv.Value.Games >= Constants.Stats.MinHeadToHeadGames)
            .GroupBy(kv => kv.Key.Self)
            .Select(group => new
            {
                PlayerId = group.Key,
                Worst = group
                    .Select(kv => new Rivalry(kv.Key.Opp, kv.Value.Losses, kv.Value.Games))
                    .OrderByDescending(r => r.LossRatio)
                    .ThenByDescending(r => r.Games)
                    .ThenBy(r => r.OpponentPlayerId)
                    .First()
            })
            .Where(x => x.Worst.LossRatio > 0.5)
            .ToDictionary(x => x.PlayerId, x => x.Worst);
    }

    private static void Bump(
        Dictionary<(int Self, int Opp), (int Losses, int Games)> pairs, int self, int opp, bool selfLost)
    {
        var key = (self, opp);
        pairs.TryGetValue(key, out var current);
        pairs[key] = (current.Losses + (selfLost ? 1 : 0), current.Games + 1);
    }
}
