using Fotbalek.SharedKernel;
using Fotbalek.Application.Features.Stats.Core;
using Fotbalek.Contracts.Stats;

namespace Fotbalek.Application.Features.Stats.Partnerships;

/// <summary>
/// The duo with the highest win rate as teammates. Reported as both players (multi-holder), with each holder naming the partner.
/// </summary>
public class BestFriendStat : StatBase
{
    public override StatKey Key => StatKey.BestFriend;
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

        var top = qualified.OrderByDescending(kv => (double)kv.Value.Wins / kv.Value.Games).First();
        var pct = (int)Math.Round((double)top.Value.Wins / top.Value.Games * 100);
        var record = new StatRatio(top.Value.Wins, top.Value.Games);

        var a = context.PlayersById[top.Key.A];
        var b = context.PlayersById[top.Key.B];

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
