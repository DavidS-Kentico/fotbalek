using Fotbalek.Web.Services.Stats.Core;

namespace Fotbalek.Web.Services.Stats.Margins;

public class DestroyerStat : StatBase
{
    public override string Key => "Destroyer";
    public override string Name => "Destroyer";
    public override string Emoji => "\U0001F4A5";
    public override StatTheme Theme => StatTheme.Margins;
    public override string Description => "Most wins by a 7+ goal margin";
    public override StatBadge? Badge => new("bi bi-lightning-charge-fill", "bg-danger");

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var counts = new Dictionary<int, int>();
        foreach (var match in context.Matches)
        {
            var diff = Math.Abs(match.Team1Score - match.Team2Score);
            if (diff < 7) continue;
            var winners = match.MatchPlayers.Where(mp => mp.IsWinner());
            foreach (var mp in winners)
            {
                counts.TryGetValue(mp.PlayerId, out var v);
                counts[mp.PlayerId] = v + 1;
            }
        }
        return StatHelpers.TopByValue(counts, context.PlayersById, v => $"{v} dominant wins");
    }
}
