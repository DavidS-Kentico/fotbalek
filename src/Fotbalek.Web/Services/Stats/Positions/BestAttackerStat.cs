using Fotbalek.Web.Services.Stats.Core;

namespace Fotbalek.Web.Services.Stats.Positions;

public class BestAttackerStat : StatBase
{
    public override string Key => "BestAttacker";
    public override string Name => "Best ATK";
    public override string Emoji => "\U0001F3AF";
    public override StatTheme Theme => StatTheme.Positions;
    public override string Description => $"Highest goals scored per match as ATK (min {Constants.TimeThresholds.MinGamesForPositionBadge} games)";
    public override StatBadge? Badge => new("bi bi-bullseye", "bg-danger");

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var minGames = Constants.TimeThresholds.MinGamesForPositionBadge;
        var perPlayer = new Dictionary<int, (int Games, int Scored)>();

        foreach (var match in context.Matches)
        {
            foreach (var mp in match.MatchPlayers)
            {
                if (mp.Position != Constants.Positions.Attacker) continue;
                perPlayer.TryGetValue(mp.PlayerId, out var s);
                perPlayer[mp.PlayerId] = (s.Games + 1, s.Scored + match.TeamScore(mp.TeamNumber));
            }
        }

        var qualified = perPlayer.Where(kv => kv.Value.Games >= minGames).ToList();
        if (qualified.Count == 0) return [];

        var top = qualified.OrderByDescending(kv => (double)kv.Value.Scored / kv.Value.Games).First();
        var avg = (double)top.Value.Scored / top.Value.Games;
        return [context.PlayersById[top.Key].ToHolder((int)Math.Round(avg * 10), $"{avg:F1} scored/game")];
    }
}
