using Fotbalek.Web.Data.Entities;

namespace Fotbalek.Web.Services.Stats.Core;

/// <summary>
/// Shared helpers used by stat implementations to keep individual stat classes terse and free of duplicated plumbing.
/// </summary>
internal static class StatHelpers
{
    public static StatHolder ToHolder(this Player player, int value, string displayValue, string? detail = null) =>
        new(player.Id, player.Name, player.AvatarId, value, displayValue, detail);

    public static int TeamScore(this Match match, int teamNumber) =>
        teamNumber == 1 ? match.Team1Score : match.Team2Score;

    public static int OpponentScore(this Match match, int teamNumber) =>
        teamNumber == 1 ? match.Team2Score : match.Team1Score;

    public static bool IsWinner(this MatchPlayer mp) => mp.EloChange > 0;

    public static int WinningTeamNumber(this Match match) =>
        match.Team1Score > match.Team2Score ? 1 : 2;

    /// <summary>Pick the holders that are tied at the maximum value (or empty if max ≤ 0 / threshold not met).</summary>
    public static IReadOnlyList<StatHolder> TopByValue(
        Dictionary<int, int> valuesByPlayerId,
        IReadOnlyDictionary<int, Player> playersById,
        Func<int, string> displayValueFor,
        int minimumValue = 1)
    {
        if (valuesByPlayerId.Count == 0) return [];
        var max = valuesByPlayerId.Values.Max();
        if (max < minimumValue) return [];
        return valuesByPlayerId
            .Where(kv => kv.Value == max)
            .Select(kv => playersById[kv.Key].ToHolder(max, displayValueFor(max)))
            .ToList();
    }

    public static IReadOnlyList<StatHolder> SingleHolder(StatHolder? holder) =>
        holder is null ? [] : [holder];

    /// <summary>Iterate all valid (winningPair, losingPair) combinations from a match — skipping malformed matches.</summary>
    public static bool TryGetTeams(this Match match, out List<MatchPlayer> winners, out List<MatchPlayer> losers)
    {
        var team1 = match.MatchPlayers.Where(mp => mp.TeamNumber == 1).ToList();
        var team2 = match.MatchPlayers.Where(mp => mp.TeamNumber == 2).ToList();
        if (team1.Count != 2 || team2.Count != 2)
        {
            winners = [];
            losers = [];
            return false;
        }
        var team1Won = match.Team1Score > match.Team2Score;
        winners = team1Won ? team1 : team2;
        losers = team1Won ? team2 : team1;
        return true;
    }
}
