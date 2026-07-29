using Fotbalek.SharedKernel;
using Fotbalek.Application.Features.Stats.Core;
using Fotbalek.Contracts.Stats;

namespace Fotbalek.Application.Features.Stats.Special;

public class TomkoMemorialStat : StatBase
{
    public override StatKey Key => StatKey.TomkoMemorial;
    public override StatTheme Theme => StatTheme.Special;

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var maxByPlayer = context.Matches
            .SelectMany(m => m.MatchPlayers.Select(mp => new { mp.PlayerId, m.PlayedAt.Date }))
            .GroupBy(x => new { x.PlayerId, x.Date })
            .Select(g => new { g.Key.PlayerId, Count = g.Count() })
            .GroupBy(x => x.PlayerId)
            .ToDictionary(g => g.Key, g => g.Max(x => x.Count));

        return StatHelpers.TopByValue(maxByPlayer, context.PlayersById, minimumValue: Constants.TimeThresholds.MinGamesForTomkoBadge);
    }
}
