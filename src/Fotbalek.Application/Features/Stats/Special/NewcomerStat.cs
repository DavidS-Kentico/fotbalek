using Fotbalek.SharedKernel;
using Fotbalek.Application.Features.Stats.Core;
using Fotbalek.Contracts.Stats;

namespace Fotbalek.Application.Features.Stats.Special;

public class NewcomerStat : StatBase
{
    public override StatKey Key => StatKey.Newcomer;
    public override StatTheme Theme => StatTheme.Special;

    public override bool Applies(StatContext context) => context.IsAllTime;

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var threshold = DateTimeOffset.UtcNow.AddDays(-Constants.TimeThresholds.RecentActivityDays);
        return context.ActivePlayers
            .Where(p => p.CreatedAt >= threshold)
            // Membership is the whole stat — there is nothing to rank holders by.
            .Select(p => p.ToHolder(0))
            .ToList();
    }
}
