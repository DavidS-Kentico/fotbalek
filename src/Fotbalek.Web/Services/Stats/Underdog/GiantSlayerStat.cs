using Fotbalek.Web.Services.Stats.Core;

namespace Fotbalek.Web.Services.Stats.Underdog;

/// <summary>
/// Most wins where the player's team had a combined ELO at least 100 lower than their opponents at match start.
/// </summary>
public class GiantSlayerStat : StatBase
{
    private const int EloGap = 100;

    public override string Key => "GiantSlayer";
    public override string Name => "Giant Slayer";
    public override string Emoji => "\U0001F5E1";
    public override StatTheme Theme => StatTheme.Underdog;
    public override string Description => $"Most wins where your team was {EloGap}+ ELO underdogs";

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var counts = new Dictionary<int, int>();
        foreach (var match in context.Matches)
        {
            if (!match.TryGetTeams(out var winners, out var losers)) continue;
            var winnersElo = winners.Sum(mp => mp.EloBefore);
            var losersElo = losers.Sum(mp => mp.EloBefore);
            if (losersElo - winnersElo < EloGap) continue;
            foreach (var w in winners)
            {
                counts.TryGetValue(w.PlayerId, out var v);
                counts[w.PlayerId] = v + 1;
            }
        }
        return StatHelpers.TopByValue(counts, context.PlayersById, v => $"{v} upset wins");
    }
}
