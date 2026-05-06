using Fotbalek.Web.Services.Stats.Core;

namespace Fotbalek.Web.Services.Stats.EloSwings;

public class BiggestEloWinStat : StatBase
{
    public override string Key => "BiggestEloWin";
    public override string Name => "Biggest Win";
    public override string Emoji => "\U0001F680";
    public override StatTheme Theme => StatTheme.EloSwings;
    public override string Description => "Largest single-match ELO gain";

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var teams = context.Matches
            .SelectMany(m => m.MatchPlayers)
            .GroupBy(mp => new { mp.MatchId, mp.TeamNumber })
            .Select(g => new { Change = g.First().EloChange, Players = g.Select(mp => mp.PlayerId).ToList() })
            .ToList();

        if (teams.Count == 0) return [];
        var max = teams.Max(t => t.Change);
        if (max <= 0) return [];

        return teams
            .Where(t => t.Change == max)
            .SelectMany(t => t.Players)
            .Distinct()
            .Select(pid => context.PlayersById[pid].ToHolder(max, $"+{max} ELO in one match"))
            .ToList();
    }
}
