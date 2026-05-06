using Fotbalek.Web.Services.Stats.Core;

namespace Fotbalek.Web.Services.Stats.Streaks;

public class HotStreakStat : StatBase
{
    public override string Key => "HotStreak";
    public override string Name => "Hot Streak";
    public override string Emoji => "\U0001F525";
    public override StatTheme Theme => StatTheme.Streaks;
    public override string Description => "Longest active winning streak (min 3 wins)";
    public override StatBadge? Badge => new("bi bi-fire", "bg-danger");

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var streaks = StreakComputer.Compute(context);
        var top = streaks
            .Where(s => s.Value.CurrentWinStreak >= 3)
            .OrderByDescending(s => s.Value.CurrentWinStreak)
            .FirstOrDefault();
        if (top.Value is null) return [];
        var v = top.Value.CurrentWinStreak;
        return [context.PlayersById[top.Key].ToHolder(v, $"{v} wins in a row")];
    }
}
