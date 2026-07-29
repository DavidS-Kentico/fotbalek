using Fotbalek.SharedKernel;
using Fotbalek.Application.Features.Stats.Core;
using Fotbalek.Contracts.Stats;

namespace Fotbalek.Application.Features.Stats.Positions;

public class BestAttackerStat : StatBase
{
    public override StatKey Key => StatKey.BestAttacker;
    public override StatTheme Theme => StatTheme.Positions;

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var minGames = Constants.TimeThresholds.MinGamesForPositionBadge;
        var perPlayer = new Dictionary<int, (int Games, int Scored)>();

        foreach (var match in context.Matches)
        {
            foreach (var mp in match.MatchPlayers)
            {
                if (mp.Position != Constants.Positions.Attacker) continue;
                perPlayer.TryGetValue(mp.PlayerId, out var s);
                perPlayer[mp.PlayerId] = (s.Games + 1, s.Scored + match.TeamScore(mp.TeamNumber));
            }
        }

        var qualified = perPlayer.Where(kv => kv.Value.Games >= minGames).ToList();
        if (qualified.Count == 0) return [];

        var top = qualified.OrderByDescending(kv => (double)kv.Value.Scored / kv.Value.Games).First();
        var avg = (double)top.Value.Scored / top.Value.Games;
        // Value keeps a tenth of a goal of resolution so ties break the way the average orders them.
        return [context.PlayersById[top.Key].ToHolder(
            (int)Math.Round(avg * 10),
            ratio: new StatRatio(top.Value.Scored, top.Value.Games))];
    }
}
