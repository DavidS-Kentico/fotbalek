using Fotbalek.Application.Features.Stats.Core;
using Fotbalek.Contracts.Stats;

namespace Fotbalek.Application.Features.Stats.Rivalries;

/// <summary>
/// For each player, finds the opponent who beats them most often (<see cref="NemesisRule"/> — shared
/// with the NemesisBeaten notification). Reports the player whose nemesis dominates them hardest.
/// </summary>
public class NemesisStat : StatBase
{
    public override StatKey Key => StatKey.Nemesis;
    public override StatTheme Theme => StatTheme.Rivalries;

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var worstPerPlayer = NemesisRule.WorstPerPlayer(context.Matches);
        if (worstPerPlayer.Count == 0) return [];

        // Leaderboard entry: the team-wide worst ratio, keeping epsilon co-holders.
        var topRatio = worstPerPlayer.Values.Max(r => r.LossRatio);
        return worstPerPlayer
            .Where(kv => Math.Abs(kv.Value.LossRatio - topRatio) < 0.0001)
            .Select(kv => context.PlayersById[kv.Key].ToHolder(
                (int)Math.Round(kv.Value.LossRatio * 100),
                detail: context.PlayersById[kv.Value.OpponentPlayerId].Name,
                ratio: new StatRatio(kv.Value.Losses, kv.Value.Games)))
            .ToList();
    }
}
