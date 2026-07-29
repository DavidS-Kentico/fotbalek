using Fotbalek.Application.Features.Stats.Core;
using Fotbalek.Contracts.Stats;

namespace Fotbalek.Application.Features.Stats.Margins;

public class LuckerStat : StatBase
{
    public override StatKey Key => StatKey.Lucker;
    public override StatTheme Theme => StatTheme.Margins;

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var counts = new Dictionary<int, int>();
        foreach (var match in context.Matches)
        {
            foreach (var mp in match.MatchPlayers)
            {
                if (mp.IsWinner()) continue;
                var teamScore = match.TeamScore(mp.TeamNumber);
                var oppScore = match.OpponentScore(mp.TeamNumber);
                if (teamScore == 1 && oppScore == 10)
                {
                    counts.TryGetValue(mp.PlayerId, out var v);
                    counts[mp.PlayerId] = v + 1;
                }
            }
        }
        return StatHelpers.TopByValue(counts, context.PlayersById);
    }
}
