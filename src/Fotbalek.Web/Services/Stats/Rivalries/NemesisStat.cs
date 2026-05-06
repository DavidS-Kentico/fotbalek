using Fotbalek.Web.Services.Stats.Core;

namespace Fotbalek.Web.Services.Stats.Rivalries;

/// <summary>
/// For each player, finds the opponent who beats them most often. Reports the player whose nemesis dominates them hardest.
/// </summary>
public class NemesisStat : StatBase
{
    private const int MinHeadToHeadGames = 4;

    public override string Key => "Nemesis";
    public override string Name => "Nemesis";
    public override string Emoji => "\U0001F47F";
    public override StatTheme Theme => StatTheme.Rivalries;
    public override string Description => $"Most lopsided losing record against a single opponent (min {MinHeadToHeadGames} games)";

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        // (player, opponent) → (lossesByPlayer, totalGames)
        var pairs = new Dictionary<(int Self, int Opp), (int Losses, int Games)>();

        foreach (var match in context.Matches)
        {
            var team1 = match.MatchPlayers.Where(mp => mp.TeamNumber == 1).ToList();
            var team2 = match.MatchPlayers.Where(mp => mp.TeamNumber == 2).ToList();
            if (team1.Count == 0 || team2.Count == 0) continue;
            var team1Won = match.Team1Score > match.Team2Score;

            foreach (var p1 in team1)
            {
                foreach (var p2 in team2)
                {
                    Bump(pairs, p1.PlayerId, p2.PlayerId, !team1Won);
                    Bump(pairs, p2.PlayerId, p1.PlayerId, team1Won);
                }
            }
        }

        // Best (worst) nemesis per player
        var perPlayer = pairs
            .Where(kv => kv.Value.Games >= MinHeadToHeadGames)
            .GroupBy(kv => kv.Key.Self)
            .Select(g =>
            {
                var worst = g.OrderByDescending(x => (double)x.Value.Losses / x.Value.Games).First();
                return new
                {
                    PlayerId = g.Key,
                    OpponentId = worst.Key.Opp,
                    worst.Value.Losses,
                    worst.Value.Games,
                    Ratio = (double)worst.Value.Losses / worst.Value.Games
                };
            })
            .Where(x => x.Ratio > 0.5)
            .ToList();

        if (perPlayer.Count == 0) return [];

        var topRatio = perPlayer.Max(x => x.Ratio);
        return perPlayer
            .Where(x => Math.Abs(x.Ratio - topRatio) < 0.0001)
            .Select(x => context.PlayersById[x.PlayerId].ToHolder(
                (int)Math.Round(x.Ratio * 100),
                $"loses {x.Losses}/{x.Games} vs {context.PlayersById[x.OpponentId].Name}",
                detail: context.PlayersById[x.OpponentId].Name))
            .ToList();
    }

    private static void Bump(Dictionary<(int Self, int Opp), (int Losses, int Games)> pairs, int self, int opp, bool selfLost)
    {
        var key = (self, opp);
        pairs.TryGetValue(key, out var c);
        pairs[key] = (c.Losses + (selfLost ? 1 : 0), c.Games + 1);
    }
}
