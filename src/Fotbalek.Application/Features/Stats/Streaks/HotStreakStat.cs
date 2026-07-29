using Fotbalek.SharedKernel;
using Fotbalek.Application.Features.Stats.Core;
using Fotbalek.Contracts.Stats;

namespace Fotbalek.Application.Features.Stats.Streaks;

public class HotStreakStat : StatBase
{
    public override StatKey Key => StatKey.HotStreak;
    public override StatTheme Theme => StatTheme.Streaks;

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var streaks = StreakComputer.Compute(context);
        var top = streaks
            .Where(s => s.Value.CurrentWinStreak >= Constants.Stats.MinStreak)
            .OrderByDescending(s => s.Value.CurrentWinStreak)
            .FirstOrDefault();
        if (top.Value is null) return [];
        return [context.PlayersById[top.Key].ToHolder(top.Value.CurrentWinStreak)];
    }
}
