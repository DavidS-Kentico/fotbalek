using Fotbalek.Web.Services.Stats.Core;

namespace Fotbalek.Web.Services.Stats.Margins;

public class TableDiverStat : StatBase
{
    public override string Key => "TableDiver";
    public override string Name => "Table Diver";
    public override string Emoji => "\U0001F931";
    public override StatTheme Theme => StatTheme.Margins;
    public override string Description => "Most 0-10 losses";
    public override StatBadge? Badge => new("bi bi-box-arrow-down", "bg-info");

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var counts = new Dictionary<int, int>();
        foreach (var match in context.Matches)
        {
            if (Math.Max(match.Team1Score, match.Team2Score) != 10 || Math.Min(match.Team1Score, match.Team2Score) != 0) continue;
            var losers = match.MatchPlayers.Where(mp => !mp.IsWinner());
            foreach (var mp in losers)
            {
                counts.TryGetValue(mp.PlayerId, out var v);
                counts[mp.PlayerId] = v + 1;
            }
        }
        return StatHelpers.TopByValue(counts, context.PlayersById, v => $"{v} under-table losses");
    }
}
