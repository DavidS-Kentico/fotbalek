using Fotbalek.Web.Services.Stats.Core;

namespace Fotbalek.Web.Services.Stats.Activity;

/// <summary>
/// Player who has played with the most distinct partners. Surfaces "social" players who rotate teammates.
/// </summary>
public class VarietyPlayerStat : StatBase
{
    private const int MinUniquePartners = 3;

    public override string Key => "VarietyPlayer";
    public override string Name => "Variety Player";
    public override string Emoji => "\U0001F308";
    public override StatTheme Theme => StatTheme.Activity;
    public override string Description => $"Most distinct teammates (min {MinUniquePartners})";

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var partners = new Dictionary<int, HashSet<int>>();
        foreach (var match in context.Matches)
        {
            foreach (var team in new[] { 1, 2 })
            {
                var players = match.MatchPlayers.Where(mp => mp.TeamNumber == team).ToList();
                if (players.Count != 2) continue;
                if (!partners.ContainsKey(players[0].PlayerId)) partners[players[0].PlayerId] = [];
                if (!partners.ContainsKey(players[1].PlayerId)) partners[players[1].PlayerId] = [];
                partners[players[0].PlayerId].Add(players[1].PlayerId);
                partners[players[1].PlayerId].Add(players[0].PlayerId);
            }
        }

        var counts = partners.ToDictionary(kv => kv.Key, kv => kv.Value.Count);
        return StatHelpers.TopByValue(counts, context.PlayersById, v => $"{v} unique partners", minimumValue: MinUniquePartners);
    }
}
