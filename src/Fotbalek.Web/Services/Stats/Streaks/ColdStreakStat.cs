using Fotbalek.Web.Services.Stats.Core;

namespace Fotbalek.Web.Services.Stats.Streaks;

public class ColdStreakStat : StatBase
{
    public override string Key => "ColdStreak";
    public override string Name => "Cold Streak";
    public override string Emoji => "❄";
    public override StatTheme Theme => StatTheme.Streaks;
    public override string Description => "Longest active losing streak (min 3 losses)";
    public override StatBadge? Badge => new("bi bi-snow", "bg-info");

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var streaks = StreakComputer.Compute(context);
        var top = streaks
            .Where(s => s.Value.CurrentLossStreak >= 3)
            .OrderByDescending(s => s.Value.CurrentLossStreak)
            .FirstOrDefault();
        if (top.Value is null) return [];
        var v = top.Value.CurrentLossStreak;
        return [context.PlayersById[top.Key].ToHolder(v, $"{v} losses in a row")];
    }
}
