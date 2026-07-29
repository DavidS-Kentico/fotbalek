using Fotbalek.SharedKernel;
using Fotbalek.Domain.Entities;
using Fotbalek.Application.Features.Stats.Core;
using Fotbalek.Contracts.Stats;

namespace Fotbalek.Application.Features.Stats.Special;

public class CarriedStat : StatBase
{
    public override StatKey Key => StatKey.Carried;
    public override StatTheme Theme => StatTheme.Special;

    /// <summary>
    /// Identifies whether a winning pair contains a carry: the stronger partner cleared the carry ELO
    /// multiplier over the weaker and both opponents were weaker than that stronger partner. Returns the
    /// carried (weak) and carrier (strong) player ids, or null if no carry occurred.
    /// <paramref name="eloBefore"/> selects the ladder (all-time EloBefore vs seasonal SeasonEloBefore).
    /// </summary>
    public static (int CarriedId, int CarrierId)? AnalyzeCarry(MatchPlayer w1, MatchPlayer w2, MatchPlayer l1, MatchPlayer l2, Func<MatchPlayer, int> eloBefore)
    {
        var multiplier = Constants.Stats.CarryEloMultiplier;
        if (eloBefore(w2) >= eloBefore(w1) * multiplier && eloBefore(l1) < eloBefore(w2) && eloBefore(l2) < eloBefore(w2))
            return (w1.PlayerId, w2.PlayerId);
        if (eloBefore(w1) >= eloBefore(w2) * multiplier && eloBefore(l1) < eloBefore(w1) && eloBefore(l2) < eloBefore(w1))
            return (w2.PlayerId, w1.PlayerId);
        return null;
    }

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var counts = new Dictionary<int, int>();

        foreach (var match in context.Matches)
        {
            if (!match.TryGetTeams(out var winners, out var losers)) continue;
            var carry = AnalyzeCarry(winners[0], winners[1], losers[0], losers[1], context.EloBeforeOf);
            if (carry is null) continue;
            counts.TryGetValue(carry.Value.CarriedId, out var v);
            counts[carry.Value.CarriedId] = v + 1;
        }

        return StatHelpers.TopByValue(counts, context.PlayersById, minimumValue: Constants.TimeThresholds.MinGamesForCarriedBadge);
    }
}
