using Fotbalek.Web.Services.Stats.Core;

namespace Fotbalek.Web.Services.Stats.Rankings;

public class LastPlaceStat : StatBase
{
    public override string Key => "LastPlace";
    public override string Name => "Last Place";
    public override string Emoji => "\U0001F4A8";
    public override StatTheme Theme => StatTheme.Rankings;
    public override string Description => "Player with the lowest current ELO";
    public override StatBadge? Badge => new("bi bi-arrow-down", "bg-dark");

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        if (!context.IsAllTime) return [];
        var bottom = context.ActivePlayers.MinBy(p => p.Elo);
        return bottom is null ? [] : [bottom.ToHolder(bottom.Elo, $"{bottom.Elo} ELO")];
    }
}
