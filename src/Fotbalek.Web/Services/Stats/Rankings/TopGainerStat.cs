using Fotbalek.Web.Services.Stats.Core;

namespace Fotbalek.Web.Services.Stats.Rankings;

public class TopGainerStat : StatBase
{
    public override string Key => "TopGainer";
    public override string Name => "Top Gainer";
    public override string Emoji => "\U0001F4C8";
    public override StatTheme Theme => StatTheme.Rankings;
    public override string Description => "Best ELO gain in the period";

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        if (context.IsAllTime || context.Matches.Count == 0) return [];

        var totals = context.Matches
            .SelectMany(m => m.MatchPlayers)
            .GroupBy(mp => mp.PlayerId)
            .ToDictionary(g => g.Key, g => g.Sum(mp => mp.EloChange));

        if (totals.Count == 0) return [];
        var max = totals.Values.Max();
        if (max <= 0) return [];

        return totals
            .Where(kv => kv.Value == max)
            .Select(kv => context.PlayersById[kv.Key].ToHolder(max, $"+{max} ELO"))
            .ToList();
    }
}
