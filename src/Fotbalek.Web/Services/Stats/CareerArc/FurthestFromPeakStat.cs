using Fotbalek.Web.Services.Stats.Core;

namespace Fotbalek.Web.Services.Stats.CareerArc;

/// <summary>
/// Active player who is currently furthest below their all-time peak ELO. Surfaces who is in a slump versus their best self.
/// </summary>
public class FurthestFromPeakStat : StatBase
{
    private const int MinDrop = 50;

    public override string Key => "FurthestFromPeak";
    public override string Name => "Fallen Star";
    public override string Emoji => "\U0001F4C9";
    public override StatTheme Theme => StatTheme.CareerArc;
    public override string Description => $"Currently furthest below their peak ELO (min {MinDrop} drop)";

    public override bool Applies(StatContext context) => context.IsAllTime;

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var peaks = context.Matches
            .SelectMany(m => m.MatchPlayers)
            .GroupBy(mp => mp.PlayerId)
            .ToDictionary(g => g.Key, g => g.Max(mp => mp.EloAfter));

        var drops = peaks
            .Where(kv => context.PlayersById.TryGetValue(kv.Key, out var p) && p.IsActive)
            .Select(kv => new { PlayerId = kv.Key, Peak = kv.Value, Current = context.PlayersById[kv.Key].Elo })
            .Where(x => x.Peak - x.Current >= MinDrop)
            .ToList();

        if (drops.Count == 0) return [];

        var top = drops.OrderByDescending(x => x.Peak - x.Current).First();
        var drop = top.Peak - top.Current;
        return [context.PlayersById[top.PlayerId].ToHolder(drop, $"-{drop} from peak ({top.Peak} → {top.Current})")];
    }
}
