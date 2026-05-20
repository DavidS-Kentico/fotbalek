using Fotbalek.Web.Data.Entities;
using Fotbalek.Web.Services.Stats.Core;

namespace Fotbalek.Web.Services.Stats.Special;

public class CarriedStat : StatBase
{
    private const double CarryEloMultiplier = 1.2;

    public override string Key => "Carried";
    public override string Name => "Carried";
    public override string Emoji => "\U0001F91D";
    public override StatTheme Theme => StatTheme.Special;
    public override string Description => $"Wins where partner had 20%+ higher ELO and both opponents were weaker (min {Constants.TimeThresholds.MinGamesForCarriedBadge})";
    public override StatBadge? Badge => new("bi bi-people-fill", "bg-purple");

    /// <summary>
    /// Identifies whether a winning pair contains a carry: the stronger partner had 20%+ higher ELO than the weaker
    /// and both opponents were weaker than that stronger partner. Returns the carried (weak) and carrier (strong)
    /// player ids, or null if no carry occurred.
    /// </summary>
    public static (int CarriedId, int CarrierId)? AnalyzeCarry(MatchPlayer w1, MatchPlayer w2, MatchPlayer l1, MatchPlayer l2)
    {
        if (w2.EloBefore >= w1.EloBefore * CarryEloMultiplier && l1.EloBefore < w2.EloBefore && l2.EloBefore < w2.EloBefore)
            return (w1.PlayerId, w2.PlayerId);
        if (w1.EloBefore >= w2.EloBefore * CarryEloMultiplier && l1.EloBefore < w1.EloBefore && l2.EloBefore < w1.EloBefore)
            return (w2.PlayerId, w1.PlayerId);
        return null;
    }

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var counts = new Dictionary<int, int>();

        foreach (var match in context.Matches)
        {
            if (!match.TryGetTeams(out var winners, out var losers)) continue;
            var carry = AnalyzeCarry(winners[0], winners[1], losers[0], losers[1]);
            if (carry is null) continue;
            counts.TryGetValue(carry.Value.CarriedId, out var v);
            counts[carry.Value.CarriedId] = v + 1;
        }

        return StatHelpers.TopByValue(counts, context.PlayersById, v => $"{v} carried wins", minimumValue: Constants.TimeThresholds.MinGamesForCarriedBadge);
    }
}
