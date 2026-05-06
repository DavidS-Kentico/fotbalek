using Fotbalek.Web.Services.Stats.Core;

namespace Fotbalek.Web.Services.Stats.Margins;

public class LuckerStat : StatBase
{
    public override string Key => "Lucker";
    public override string Name => "Lucker";
    public override string Emoji => "\U0001F340";
    public override StatTheme Theme => StatTheme.Margins;
    public override string Description => "Most 1-10 losses (one goal scored)";
    public override StatBadge? Badge => new("bi bi-life-preserver", "bg-warning text-dark");

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var counts = new Dictionary<int, int>();
        foreach (var match in context.Matches)
        {
            foreach (var mp in match.MatchPlayers)
            {
                if (mp.IsWinner()) continue;
                var teamScore = match.TeamScore(mp.TeamNumber);
                var oppScore = match.OpponentScore(mp.TeamNumber);
                if (teamScore == 1 && oppScore == 10)
                {
                    counts.TryGetValue(mp.PlayerId, out var v);
                    counts[mp.PlayerId] = v + 1;
                }
            }
        }
        return StatHelpers.TopByValue(counts, context.PlayersById, v => $"{v} narrow losses");
    }
}
