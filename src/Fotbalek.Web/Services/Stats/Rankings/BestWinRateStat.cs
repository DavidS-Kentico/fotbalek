using Fotbalek.Web.Services.Stats.Core;

namespace Fotbalek.Web.Services.Stats.Rankings;

public class BestWinRateStat : StatBase
{
    public override string Key => "BestWinRate";
    public override string Name => "Best Win%";
    public override string Emoji => "\U0001F4CA";
    public override StatTheme Theme => StatTheme.Rankings;
    public override string Description => $"Highest win rate (min {Constants.TimeThresholds.MinGamesForPositionBadge} games)";
    public override StatBadge? Badge => new("bi bi-percent", "bg-primary");

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
        return [context.PlayersById[top.PlayerId].ToHolder(pct, $"{pct}% ({top.Wins}/{top.Games})")];
    }
}
