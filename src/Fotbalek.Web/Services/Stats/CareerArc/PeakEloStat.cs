using Fotbalek.Web.Services.Stats.Core;

namespace Fotbalek.Web.Services.Stats.CareerArc;

/// <summary>
/// Highest ELO any player has ever reached. Computed from EloAfter across all match plays in the context.
/// </summary>
public class PeakEloStat : StatBase
{
    public override string Key => "PeakElo";
    public override string Name => "Peak ELO";
    public override string Emoji => "\U0001F3D4";
    public override StatTheme Theme => StatTheme.CareerArc;
    public override string Description => "Highest ELO ever reached";

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var peaks = context.Matches
            .SelectMany(m => m.MatchPlayers)
            .GroupBy(mp => mp.PlayerId)
            .Select(g => new { PlayerId = g.Key, Peak = g.Max(mp => mp.EloAfter) })
            .ToList();

        if (peaks.Count == 0) return [];

        // Always also consider the player's current ELO in case it is the historical peak (no match has dropped them since).
        var max = peaks.Max(p => p.Peak);
        return peaks
            .Where(p => p.Peak == max)
            .Select(p => context.PlayersById[p.PlayerId].ToHolder(p.Peak, $"{p.Peak} ELO peak"))
            .ToList();
    }
}
