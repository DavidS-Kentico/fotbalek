using Fotbalek.Application.Features.Stats.Core;
using Fotbalek.Contracts.Stats;

namespace Fotbalek.Application.Features.Stats.Rankings;

public class LastPlaceStat : StatBase
{
    public override string Key => "LastPlace";
    public override string Name => "Last Place";
    public override string Emoji => "\U0001F4A8";
    public override StatTheme Theme => StatTheme.Rankings;
    public override string Description => "Player with the lowest current ELO";
    public override StatBadge? Badge => new("bi bi-arrow-down", "bg-dark");

    // "Current ELO of the selected ladder" is well-defined for a full season too.
    public override bool Applies(StatContext context) => context.IsFullScope;

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var bottom = context.ActivePlayers.MinBy(context.CurrentEloOf);
        return bottom is null ? [] : [bottom.ToHolder(context.CurrentEloOf(bottom), $"{context.CurrentEloOf(bottom)} ELO")];
    }
}
