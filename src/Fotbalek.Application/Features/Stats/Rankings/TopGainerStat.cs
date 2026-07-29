using Fotbalek.Application.Features.Stats.Core;
using Fotbalek.Contracts.Stats;

namespace Fotbalek.Application.Features.Stats.Rankings;

public class TopGainerStat : StatBase
{
    public override StatKey Key => StatKey.TopGainer;
    public override StatTheme Theme => StatTheme.Rankings;

    public override bool Applies(StatContext context) => !context.IsAllTime;

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        if (context.Matches.Count == 0) return [];

        var totals = context.Matches
            .SelectMany(m => m.MatchPlayers)
            .GroupBy(mp => mp.PlayerId)
            .ToDictionary(g => g.Key, g => g.Sum(context.EloChangeOf));

        if (totals.Count == 0) return [];
        var max = totals.Values.Max();
        if (max <= 0) return [];

        return totals
            .Where(kv => kv.Value == max)
            .Select(kv => context.PlayersById[kv.Key].ToHolder(max))
            .ToList();
    }
}
