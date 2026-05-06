using Fotbalek.Web.Services.Stats.Core;

namespace Fotbalek.Web.Services.Stats.Margins;

public class CardiacKidStat : StatBase
{
    private const int MinCloseGames = 5;

    public override string Key => "CardiacKid";
    public override string Name => "Cardiac Kid";
    public override string Emoji => "\U0001F493";
    public override StatTheme Theme => StatTheme.Margins;
    public override string Description => $"Best win rate in 1-goal games (min {MinCloseGames} such games)";

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var stats = new Dictionary<int, (int Wins, int Games)>();
        foreach (var match in context.Matches)
        {
            if (Math.Abs(match.Team1Score - match.Team2Score) != 1) continue;
            foreach (var mp in match.MatchPlayers)
            {
                stats.TryGetValue(mp.PlayerId, out var s);
                stats[mp.PlayerId] = (s.Wins + (mp.IsWinner() ? 1 : 0), s.Games + 1);
            }
        }

        var qualified = stats.Where(kv => kv.Value.Games >= MinCloseGames).ToList();
        if (qualified.Count == 0) return [];

        var top = qualified.OrderByDescending(kv => (double)kv.Value.Wins / kv.Value.Games).First();
        var pct = (int)Math.Round((double)top.Value.Wins / top.Value.Games * 100);
        return [context.PlayersById[top.Key].ToHolder(pct, $"{pct}% in close games ({top.Value.Wins}/{top.Value.Games})")];
    }
}
