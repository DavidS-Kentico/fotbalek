using Fotbalek.SharedKernel;
using Fotbalek.Application.Features.Stats.Core;
using Fotbalek.Contracts.Stats;

namespace Fotbalek.Application.Features.Stats.Partnerships;

public class WorstFriendStat : StatBase
{
    public override StatKey Key => StatKey.WorstFriend;
    public override StatTheme Theme => StatTheme.Partnerships;

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var pairs = new Dictionary<(int A, int B), (int Wins, int Games)>();
        foreach (var match in context.Matches)
        {
            ProcessTeam(pairs, match, 1);
            ProcessTeam(pairs, match, 2);
        }

        var qualified = pairs.Where(kv => kv.Value.Games >= Constants.Stats.MinPairGames).ToList();
        if (qualified.Count == 0) return [];

        var bottom = qualified.OrderBy(kv => (double)kv.Value.Wins / kv.Value.Games).First();
        var pct = (int)Math.Round((double)bottom.Value.Wins / bottom.Value.Games * 100);
        if (pct >= 50) return [];
        var record = new StatRatio(bottom.Value.Wins, bottom.Value.Games);

        var a = context.PlayersById[bottom.Key.A];
        var b = context.PlayersById[bottom.Key.B];

        return new[]
        {
            a.ToHolder(pct, detail: b.Name, ratio: record),
            b.ToHolder(pct, detail: a.Name, ratio: record)
        };
    }

    private static void ProcessTeam(Dictionary<(int A, int B), (int Wins, int Games)> pairs, Domain.Entities.Match match, int teamNumber)
    {
        var team = match.MatchPlayers.Where(mp => mp.TeamNumber == teamNumber).OrderBy(mp => mp.PlayerId).ToList();
        if (team.Count != 2) return;
        var won = team[0].IsWinner();
        var key = (team[0].PlayerId, team[1].PlayerId);
        pairs.TryGetValue(key, out var c);
        pairs[key] = (c.Wins + (won ? 1 : 0), c.Games + 1);
    }
}
