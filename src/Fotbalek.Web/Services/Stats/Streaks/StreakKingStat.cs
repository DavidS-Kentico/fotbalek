using Fotbalek.Web.Services.Stats.Core;

namespace Fotbalek.Web.Services.Stats.Streaks;

public class StreakKingStat : StatBase
{
    public override string Key => "StreakKing";
    public override string Name => "Streak King";
    public override string Emoji => "\U0001F451";
    public override StatTheme Theme => StatTheme.Streaks;
    public override string Description => "Longest winning streak in the period (min 3 wins)";
    public override StatBadge? Badge => new("bi bi-gem", "bg-primary");

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var streaks = StreakComputer.Compute(context);
        var top = streaks
            .Where(s => s.Value.LongestWinStreak >= 3)
            .OrderByDescending(s => s.Value.LongestWinStreak)
            .FirstOrDefault();
        if (top.Value is null) return [];
        var v = top.Value.LongestWinStreak;
        return [context.PlayersById[top.Key].ToHolder(v, $"{v} wins in a row")];
    }
}
