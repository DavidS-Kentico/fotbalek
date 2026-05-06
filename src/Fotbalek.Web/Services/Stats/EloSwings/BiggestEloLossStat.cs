using Fotbalek.Web.Services.Stats.Core;

namespace Fotbalek.Web.Services.Stats.EloSwings;

public class BiggestEloLossStat : StatBase
{
    public override string Key => "BiggestEloLoss";
    public override string Name => "Biggest Loss";
    public override string Emoji => "\U0001F4C9";
    public override StatTheme Theme => StatTheme.EloSwings;
    public override string Description => "Largest single-match ELO loss";

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var teams = context.Matches
            .SelectMany(m => m.MatchPlayers)
            .GroupBy(mp => new { mp.MatchId, mp.TeamNumber })
            .Select(g => new { Change = g.First().EloChange, Players = g.Select(mp => mp.PlayerId).ToList() })
            .ToList();

        if (teams.Count == 0) return [];
        var min = teams.Min(t => t.Change);
        if (min >= 0) return [];

        return teams
            .Where(t => t.Change == min)
            .SelectMany(t => t.Players)
            .Distinct()
            .Select(pid => context.PlayersById[pid].ToHolder(min, $"{min} ELO in one match"))
            .ToList();
    }
}
