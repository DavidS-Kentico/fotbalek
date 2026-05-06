using Fotbalek.Web.Services.Stats.Core;

namespace Fotbalek.Web.Services.Stats.Special;

public class CarriedStat : StatBase
{
    public override string Key => "Carried";
    public override string Name => "Carried";
    public override string Emoji => "\U0001F91D";
    public override StatTheme Theme => StatTheme.Special;
    public override string Description => $"Wins where partner had 20%+ higher ELO and both opponents were weaker (min {Constants.TimeThresholds.MinGamesForCarriedBadge})";
    public override StatBadge? Badge => new("bi bi-people-fill", "bg-purple");

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var counts = new Dictionary<int, int>();

        foreach (var match in context.Matches)
        {
            if (!match.TryGetTeams(out var winners, out var losers)) continue;
            var w1 = winners[0];
            var w2 = winners[1];
            var l1 = losers[0];
            var l2 = losers[1];

            if (w2.EloBefore >= w1.EloBefore * 1.2 && l1.EloBefore < w2.EloBefore && l2.EloBefore < w2.EloBefore)
            {
                counts.TryGetValue(w1.PlayerId, out var v);
                counts[w1.PlayerId] = v + 1;
            }
            if (w1.EloBefore >= w2.EloBefore * 1.2 && l1.EloBefore < w1.EloBefore && l2.EloBefore < w1.EloBefore)
            {
                counts.TryGetValue(w2.PlayerId, out var v);
                counts[w2.PlayerId] = v + 1;
            }
        }

        return StatHelpers.TopByValue(counts, context.PlayersById, v => $"{v} carried wins", minimumValue: Constants.TimeThresholds.MinGamesForCarriedBadge);
    }
}
