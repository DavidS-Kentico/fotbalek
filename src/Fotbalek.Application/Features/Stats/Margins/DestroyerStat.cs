using Fotbalek.SharedKernel;
using Fotbalek.Application.Features.Stats.Core;
using Fotbalek.Contracts.Stats;

namespace Fotbalek.Application.Features.Stats.Margins;

public class DestroyerStat : StatBase
{
    public override StatKey Key => StatKey.Destroyer;
    public override StatTheme Theme => StatTheme.Margins;

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var counts = new Dictionary<int, int>();
        foreach (var match in context.Matches)
        {
            var diff = Math.Abs(match.Team1Score - match.Team2Score);
            if (diff < Constants.Stats.DominantWinMargin) continue;
            var winners = match.MatchPlayers.Where(mp => mp.IsWinner());
            foreach (var mp in winners)
            {
                counts.TryGetValue(mp.PlayerId, out var v);
                counts[mp.PlayerId] = v + 1;
            }
        }
        return StatHelpers.TopByValue(counts, context.PlayersById);
    }
}
