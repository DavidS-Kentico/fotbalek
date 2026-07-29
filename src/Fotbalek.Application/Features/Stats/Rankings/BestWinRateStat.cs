using Fotbalek.SharedKernel;
using Fotbalek.Application.Features.Stats.Core;
using Fotbalek.Contracts.Stats;

namespace Fotbalek.Application.Features.Stats.Rankings;

public class BestWinRateStat : StatBase
{
    public override StatKey Key => StatKey.BestWinRate;
    public override StatTheme Theme => StatTheme.Rankings;

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var minGames = Constants.TimeThresholds.MinGamesForPositionBadge;
        var stats = context.Matches
            .SelectMany(m => m.MatchPlayers)
            .GroupBy(mp => mp.PlayerId)
            .Select(g => new { PlayerId = g.Key, Games = g.Count(), Wins = g.Count(mp => mp.IsWinner()) })
            .Where(s => s.Games >= minGames)
            .ToList();

        if (stats.Count == 0) return [];
        var top = stats.OrderByDescending(s => (double)s.Wins / s.Games).First();
        var pct = (int)Math.Round((double)top.Wins / top.Games * 100);
        return [context.PlayersById[top.PlayerId].ToHolder(pct, ratio: new StatRatio(top.Wins, top.Games))];
    }
}
