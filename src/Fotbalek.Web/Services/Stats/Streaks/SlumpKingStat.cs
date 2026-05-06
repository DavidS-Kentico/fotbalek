using Fotbalek.Web.Services.Stats.Core;

namespace Fotbalek.Web.Services.Stats.Streaks;

public class SlumpKingStat : StatBase
{
    public override string Key => "SlumpKing";
    public override string Name => "Slump King";
    public override string Emoji => "\U0001F926";
    public override StatTheme Theme => StatTheme.Streaks;
    public override string Description => "Longest losing streak in the period (min 3 losses)";
    public override StatBadge? Badge => new("bi bi-thermometer-snow", "bg-dark");

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var streaks = StreakComputer.Compute(context);
        var top = streaks
            .Where(s => s.Value.LongestLossStreak >= 3)
            .OrderByDescending(s => s.Value.LongestLossStreak)
            .FirstOrDefault();
        if (top.Value is null) return [];
        var v = top.Value.LongestLossStreak;
        return [context.PlayersById[top.Key].ToHolder(v, $"{v} losses in a row")];
    }
}
