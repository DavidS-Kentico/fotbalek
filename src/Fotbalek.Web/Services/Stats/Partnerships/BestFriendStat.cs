using Fotbalek.Web.Services.Stats.Core;

namespace Fotbalek.Web.Services.Stats.Partnerships;

/// <summary>
/// The duo with the highest win rate as teammates. Reported as both players (multi-holder), with each holder showing the partner.
/// </summary>
public class BestFriendStat : StatBase
{
    private const int MinPairGames = 5;

    public override string Key => "BestFriend";
    public override string Name => "Best Friend";
    public override string Emoji => "\U0001F46F";
    public override StatTheme Theme => StatTheme.Partnerships;
    public override string Description => $"Highest win rate as a duo (min {MinPairGames} games together)";

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

        var top = qualified.OrderByDescending(kv => (double)kv.Value.Wins / kv.Value.Games).First();
        var pct = (int)Math.Round((double)top.Value.Wins / top.Value.Games * 100);

        var a = context.PlayersById[top.Key.A];
        var b = context.PlayersById[top.Key.B];

        return new[]
        {
            a.ToHolder(pct, $"{pct}% with {b.Name} ({top.Value.Wins}/{top.Value.Games})", detail: b.Name),
            b.ToHolder(pct, $"{pct}% with {a.Name} ({top.Value.Wins}/{top.Value.Games})", detail: a.Name)
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
