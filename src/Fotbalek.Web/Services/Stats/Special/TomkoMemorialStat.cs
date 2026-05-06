using Fotbalek.Web.Services.Stats.Core;

namespace Fotbalek.Web.Services.Stats.Special;

public class TomkoMemorialStat : StatBase
{
    public override string Key => "TomkoMemorial";
    public override string Name => "Tomko Memorial";
    public override string Emoji => "\U0001F3C6";
    public override StatTheme Theme => StatTheme.Special;
    public override string Description => $"Most games played in a single day (min {Constants.TimeThresholds.MinGamesForTomkoBadge})";
    public override StatBadge? Badge => new("bi bi-calendar-event", "bg-warning text-dark");

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var minGames = Constants.TimeThresholds.MinGamesForTomkoBadge;

        var maxByPlayer = context.Matches
            .SelectMany(m => m.MatchPlayers.Select(mp => new { mp.PlayerId, m.PlayedAt.Date }))
            .GroupBy(x => new { x.PlayerId, x.Date })
            .Select(g => new { g.Key.PlayerId, Count = g.Count() })
            .GroupBy(x => x.PlayerId)
            .ToDictionary(g => g.Key, g => g.Max(x => x.Count));

        return StatHelpers.TopByValue(maxByPlayer, context.PlayersById, v => $"{v} games in one day", minimumValue: minGames);
    }
}
