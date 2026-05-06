using Fotbalek.Web.Services.Stats.Core;

namespace Fotbalek.Web.Services.Stats.Partnerships;

public class WorstFriendStat : StatBase
{
    private const int MinPairGames = 5;

    public override string Key => "WorstFriend";
    public override string Name => "Toxic Duo";
    public override string Emoji => "☠";
    public override StatTheme Theme => StatTheme.Partnerships;
    public override string Description => $"Lowest win rate as a duo (min {MinPairGames} games together)";

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var pairs = new Dictionary<(int A, int B), (int Wins, int Games)>();
        foreach (var match in context.Matches)
        {
            ProcessTeam(pairs, match, 1);
            ProcessTeam(pairs, match, 2);
        }

        var qualified = pairs.Where(kv => kv.Value.Games >= MinPairGames).ToList();
        if (qualified.Count == 0) return [];

        var bottom = qualified.OrderBy(kv => (double)kv.Value.Wins / kv.Value.Games).First();
        var pct = (int)Math.Round((double)bottom.Value.Wins / bottom.Value.Games * 100);
        if (pct >= 50) return [];

        var a = context.PlayersById[bottom.Key.A];
        var b = context.PlayersById[bottom.Key.B];

        return new[]
        {
            a.ToHolder(pct, $"{pct}% with {b.Name} ({bottom.Value.Wins}/{bottom.Value.Games})", detail: b.Name),
            b.ToHolder(pct, $"{pct}% with {a.Name} ({bottom.Value.Wins}/{bottom.Value.Games})", detail: a.Name)
        };
    }

    private static void ProcessTeam(Dictionary<(int A, int B), (int Wins, int Games)> pairs, Data.Entities.Match match, int teamNumber)
    {
        var team = match.MatchPlayers.Where(mp => mp.TeamNumber == teamNumber).OrderBy(mp => mp.PlayerId).ToList();
        if (team.Count != 2) return;
        var won = team[0].IsWinner();
        var key = (team[0].PlayerId, team[1].PlayerId);
        pairs.TryGetValue(key, out var c);
        pairs[key] = (c.Wins + (won ? 1 : 0), c.Games + 1);
    }
}
