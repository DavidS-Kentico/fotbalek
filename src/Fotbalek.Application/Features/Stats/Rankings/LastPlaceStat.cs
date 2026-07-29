using Fotbalek.Application.Features.Stats.Core;
using Fotbalek.Contracts.Stats;

namespace Fotbalek.Application.Features.Stats.Rankings;

public class LastPlaceStat : StatBase
{
    public override StatKey Key => StatKey.LastPlace;
    public override StatTheme Theme => StatTheme.Rankings;

    // "Current ELO of the selected ladder" is well-defined for a full season too.
    public override bool Applies(StatContext context) => context.IsFullScope;

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var bottom = context.ActivePlayers.MinBy(context.CurrentEloOf);
        return bottom is null ? [] : [bottom.ToHolder(context.CurrentEloOf(bottom))];
    }
}
