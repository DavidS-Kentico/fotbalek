using Fotbalek.Application.Features.Stats.Core;
using Fotbalek.Contracts.Stats;

namespace Fotbalek.Application.Features.Stats.EloSwings;

public class BiggestEloWinStat : StatBase
{
    public override StatKey Key => StatKey.BiggestEloWin;
    public override StatTheme Theme => StatTheme.EloSwings;

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var teams = context.Matches
            .SelectMany(m => m.MatchPlayers)
            .GroupBy(mp => new { mp.MatchId, mp.TeamNumber })
            .Select(g => new { Change = context.EloChangeOf(g.First()), Players = g.Select(mp => mp.PlayerId).ToList() })
            .ToList();

        if (teams.Count == 0) return [];
        var max = teams.Max(t => t.Change);
        if (max <= 0) return [];

        return teams
            .Where(t => t.Change == max)
            .SelectMany(t => t.Players)
            .Distinct()
            .Select(pid => context.PlayersById[pid].ToHolder(max))
            .ToList();
    }
}
