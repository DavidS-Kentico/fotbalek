using Fotbalek.Web.Services.Stats.Core;

namespace Fotbalek.Web.Services.Stats.Special;

public class NewcomerStat : StatBase
{
    public override string Key => "Newcomer";
    public override string Name => "Newcomer";
    public override string Emoji => "✨";
    public override StatTheme Theme => StatTheme.Special;
    public override string Description => $"Joined in the last {Constants.TimeThresholds.RecentActivityDays} days";
    public override StatBadge? Badge => new("bi bi-stars", "bg-success");

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        if (!context.IsAllTime) return [];
        var threshold = DateTimeOffset.UtcNow.AddDays(-Constants.TimeThresholds.RecentActivityDays);
        return context.ActivePlayers
            .Where(p => p.CreatedAt >= threshold)
            .Select(p => p.ToHolder(0, "joined recently"))
            .ToList();
    }
}
