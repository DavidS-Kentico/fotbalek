using Fotbalek.SharedKernel;
using Fotbalek.Application.Features.Stats.Core;
using Fotbalek.Contracts.Stats;

namespace Fotbalek.Application.Features.Stats.Underdog;

/// <summary>
/// Most wins where the player's team had a combined ELO at least an underdog gap lower than their opponents at match start.
/// </summary>
public class GiantSlayerStat : StatBase
{
    public override StatKey Key => StatKey.GiantSlayer;
    public override StatTheme Theme => StatTheme.Underdog;

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var counts = new Dictionary<int, int>();
        foreach (var match in context.Matches)
        {
            if (!match.TryGetTeams(out var winners, out var losers)) continue;
            var winnersElo = winners.Sum(context.EloBeforeOf);
            var losersElo = losers.Sum(context.EloBeforeOf);
            if (losersElo - winnersElo < Constants.Stats.UnderdogEloGap) continue;
            foreach (var w in winners)
            {
                counts.TryGetValue(w.PlayerId, out var v);
                counts[w.PlayerId] = v + 1;
            }
        }
        return StatHelpers.TopByValue(counts, context.PlayersById);
    }
}
