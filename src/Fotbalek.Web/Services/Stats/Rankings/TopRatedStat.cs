using Fotbalek.Web.Services.Stats.Core;

namespace Fotbalek.Web.Services.Stats.Rankings;

public class TopRatedStat : StatBase
{
    public override string Key => "TopRated";
    public override string Name => "Top Rated";
    public override string Emoji => "⭐";
    public override StatTheme Theme => StatTheme.Rankings;
    public override string Description => "Player with the highest current ELO";
    public override StatBadge? Badge => new("bi bi-star-fill", "bg-warning text-dark");

    public override bool Applies(StatContext context) => context.IsAllTime;

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var top = context.ActivePlayers.MaxBy(p => p.Elo);
        return top is null ? [] : [top.ToHolder(top.Elo, $"{top.Elo} ELO")];
    }
}
