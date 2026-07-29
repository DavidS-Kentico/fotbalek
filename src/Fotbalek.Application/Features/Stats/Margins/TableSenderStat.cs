using Fotbalek.Application.Features.Stats.Core;
using Fotbalek.Contracts.Stats;

namespace Fotbalek.Application.Features.Stats.Margins;

public class TableSenderStat : StatBase
{
    public override StatKey Key => StatKey.TableSender;
    public override StatTheme Theme => StatTheme.Margins;

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var counts = new Dictionary<int, int>();
        foreach (var match in context.Matches)
        {
            if (Math.Max(match.Team1Score, match.Team2Score) != 10 || Math.Min(match.Team1Score, match.Team2Score) != 0) continue;
            var winners = match.MatchPlayers.Where(mp => mp.IsWinner());
            foreach (var mp in winners)
            {
                counts.TryGetValue(mp.PlayerId, out var v);
                counts[mp.PlayerId] = v + 1;
            }
        }
        return StatHelpers.TopByValue(counts, context.PlayersById);
    }
}
