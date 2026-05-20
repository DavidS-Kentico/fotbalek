using Fotbalek.Web.Services.Stats.Core;

namespace Fotbalek.Web.Services.Stats.Activity;

public class VarietyPlayerStat : StatBase
{
    public override string Key => "VarietyPlayer";
    public override string Name => "Variety Player";
    public override string Emoji => "\U0001F308";
    public override StatTheme Theme => StatTheme.Activity;
    public override string Description => "Most unique teammates across all matches";

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var counts = new Dictionary<int, int>();
        foreach (var match in context.Matches)
        {
            foreach (var team in new[] { 1, 2 })
            {
                var players = match.MatchPlayers.Where(mp => mp.TeamNumber == team).ToList();
                if (players.Count != 2) continue;
                counts.TryGetValue(players[0].PlayerId, out var v0);
                counts[players[0].PlayerId] = v0 + 1;
                counts.TryGetValue(players[1].PlayerId, out var v1);
                counts[players[1].PlayerId] = v1 + 1;
            }
        }

        return StatHelpers.TopByValue(counts, context.PlayersById, v => $"{v} different teammate pairings");
    }
}
