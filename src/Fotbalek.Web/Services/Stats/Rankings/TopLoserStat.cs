using Fotbalek.Web.Services.Stats.Core;

namespace Fotbalek.Web.Services.Stats.Rankings;

public class TopLoserStat : StatBase
{
    public override string Key => "TopLoser";
    public override string Name => "Top Loser";
    public override string Emoji => "\U0001F4C9";
    public override StatTheme Theme => StatTheme.Rankings;
    public override string Description => "Worst ELO change in the period";

    public override bool Applies(StatContext context) => !context.IsAllTime;

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        if (context.Matches.Count == 0) return [];

        var totals = context.Matches
            .SelectMany(m => m.MatchPlayers)
            .GroupBy(mp => mp.PlayerId)
            .ToDictionary(g => g.Key, g => g.Sum(mp => mp.EloChange));

        if (totals.Count == 0) return [];
        var min = totals.Values.Min();
        if (min >= 0) return [];

        return totals
            .Where(kv => kv.Value == min)
            .Select(kv => context.PlayersById[kv.Key].ToHolder(min, $"{min} ELO"))
            .ToList();
    }
}
