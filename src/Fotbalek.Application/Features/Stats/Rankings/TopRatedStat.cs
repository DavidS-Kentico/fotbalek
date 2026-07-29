using Fotbalek.Application.Features.Stats.Core;
using Fotbalek.Contracts.Stats;

namespace Fotbalek.Application.Features.Stats.Rankings;

public class TopRatedStat : StatBase
{
    public override StatKey Key => StatKey.TopRated;
    public override StatTheme Theme => StatTheme.Rankings;

    // "Current ELO of the selected ladder" is well-defined for a full season too.
    public override bool Applies(StatContext context) => context.IsFullScope;

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var top = context.ActivePlayers.MaxBy(context.CurrentEloOf);
        return top is null ? [] : [top.ToHolder(context.CurrentEloOf(top))];
    }
}
