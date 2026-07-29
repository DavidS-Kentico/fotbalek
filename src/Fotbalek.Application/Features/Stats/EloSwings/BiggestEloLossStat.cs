using Fotbalek.Application.Features.Stats.Core;
using Fotbalek.Contracts.Stats;

namespace Fotbalek.Application.Features.Stats.EloSwings;

public class BiggestEloLossStat : StatBase
{
    public override StatKey Key => StatKey.BiggestEloLoss;
    public override StatTheme Theme => StatTheme.EloSwings;

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var teams = context.Matches
            .SelectMany(m => m.MatchPlayers)
            .GroupBy(mp => new { mp.MatchId, mp.TeamNumber })
            .Select(g => new { Change = context.EloChangeOf(g.First()), Players = g.Select(mp => mp.PlayerId).ToList() })
            .ToList();

        if (teams.Count == 0) return [];
        var min = teams.Min(t => t.Change);
        if (min >= 0) return [];

        return teams
            .Where(t => t.Change == min)
            .SelectMany(t => t.Players)
            .Distinct()
            .Select(pid => context.PlayersById[pid].ToHolder(min))
            .ToList();
    }
}
