using Fotbalek.Web.Services.Stats.Core;

namespace Fotbalek.Web.Services.Stats.Margins;

public class TableSenderStat : StatBase
{
    public override string Key => "TableSender";
    public override string Name => "Table Sender";
    public override string Emoji => "\U0001F4AA";
    public override StatTheme Theme => StatTheme.Margins;
    public override string Description => "Most 10-0 wins";
    public override StatBadge? Badge => new("bi bi-box-arrow-up", "bg-success");

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var counts = new Dictionary<int, int>();
        foreach (var match in context.Matches)
        {
            if (Math.Max(match.Team1Score, match.Team2Score) != 10 || Math.Min(match.Team1Score, match.Team2Score) != 0) continue;
            var winners = match.MatchPlayers.Where(mp => mp.IsWinner());
            foreach (var mp in winners)
            {
                counts.TryGetValue(mp.PlayerId, out var v);
                counts[mp.PlayerId] = v + 1;
            }
        }
        return StatHelpers.TopByValue(counts, context.PlayersById, v => $"{v} table sends");
    }
}
