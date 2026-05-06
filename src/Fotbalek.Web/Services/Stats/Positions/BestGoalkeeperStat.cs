using Fotbalek.Web.Services.Stats.Core;

namespace Fotbalek.Web.Services.Stats.Positions;

public class BestGoalkeeperStat : StatBase
{
    public override string Key => "BestGoalkeeper";
    public override string Name => "Best GK";
    public override string Emoji => "\U0001F92F";
    public override StatTheme Theme => StatTheme.Positions;
    public override string Description => $"Lowest goals conceded per match as GK (min {Constants.TimeThresholds.MinGamesForPositionBadge} games)";
    public override StatBadge? Badge => new("bi bi-shield-fill", "bg-secondary");

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var minGames = Constants.TimeThresholds.MinGamesForPositionBadge;
        var perPlayer = new Dictionary<int, (int Games, int Conceded)>();

        foreach (var match in context.Matches)
        {
            foreach (var mp in match.MatchPlayers)
            {
                if (mp.Position != Constants.Positions.Goalkeeper) continue;
                perPlayer.TryGetValue(mp.PlayerId, out var s);
                perPlayer[mp.PlayerId] = (s.Games + 1, s.Conceded + match.OpponentScore(mp.TeamNumber));
            }
        }

        var qualified = perPlayer.Where(kv => kv.Value.Games >= minGames).ToList();
        if (qualified.Count == 0) return [];

        var top = qualified.OrderBy(kv => (double)kv.Value.Conceded / kv.Value.Games).First();
        var avg = (double)top.Value.Conceded / top.Value.Games;
        return [context.PlayersById[top.Key].ToHolder((int)Math.Round(avg * 10), $"{avg:F1} conceded/game")];
    }
}
