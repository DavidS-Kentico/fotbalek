using Fotbalek.SharedKernel;
using Fotbalek.Application.Features.Stats.Core;
using Fotbalek.Contracts.Stats;

namespace Fotbalek.Application.Features.Stats.Streaks;

public class SlumpKingStat : StatBase
{
    public override StatKey Key => StatKey.SlumpKing;
    public override StatTheme Theme => StatTheme.Streaks;

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var streaks = StreakComputer.Compute(context);
        var top = streaks
            .Where(s => s.Value.LongestLossStreak >= Constants.Stats.MinStreak)
            .OrderByDescending(s => s.Value.LongestLossStreak)
            .FirstOrDefault();
        if (top.Value is null) return [];
        return [context.PlayersById[top.Key].ToHolder(top.Value.LongestLossStreak)];
    }
}
