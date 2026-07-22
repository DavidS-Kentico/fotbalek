using Fotbalek.Application.Features.Stats.Core;
using Fotbalek.Contracts.Stats;

namespace Fotbalek.Application.Features.Stats.Rankings;

public class TopRatedStat : StatBase
{
    public override string Key => "TopRated";
    public override string Name => "Top Rated";
    public override string Emoji => "⭐";
    public override StatTheme Theme => StatTheme.Rankings;
    public override string Description => "Player with the highest current ELO";
    public override StatBadge? Badge => new("bi bi-star-fill", "bg-warning text-dark");

    // "Current ELO of the selected ladder" is well-defined for a full season too.
    public override bool Applies(StatContext context) => context.IsFullScope;

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var top = context.ActivePlayers.MaxBy(context.CurrentEloOf);
        return top is null ? [] : [top.ToHolder(context.CurrentEloOf(top), $"{context.CurrentEloOf(top)} ELO")];
    }
}
