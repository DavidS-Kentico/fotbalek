namespace Fotbalek.Web.Services;

/// <summary>
/// Service for generating player badge display information
/// </summary>
public class BadgeService
{
    /// <summary>
    /// Gets the list of badges for a specific player based on team badges
    /// </summary>
    public List<PlayerBadgeInfo> GetPlayerBadges(int playerId, TeamBadges? badges)
    {
        var result = new List<PlayerBadgeInfo>();
        if (badges == null) return result;

        if (badges.TopRated?.PlayerId == playerId)
            result.Add(new PlayerBadgeInfo("bi bi-star-fill", "bg-warning text-dark", "Top Rated - Highest ELO"));

        if (badges.HotStreak?.PlayerId == playerId)
            result.Add(new PlayerBadgeInfo("bi bi-fire", "bg-danger", $"Hot Streak - {badges.HotStreak.Value} wins"));

        if (badges.StreakKing?.PlayerId == playerId)
            result.Add(new PlayerBadgeInfo("bi bi-gem", "bg-primary", $"Streak King - {badges.StreakKing.Value} wins (all-time)"));

        if (badges.BestGoalkeeper?.PlayerId == playerId)
            result.Add(new PlayerBadgeInfo("bi bi-shield-fill", "bg-secondary", "Best Goalkeeper"));

        if (badges.BestAttacker?.PlayerId == playerId)
            result.Add(new PlayerBadgeInfo("bi bi-bullseye", "bg-danger", "Best Attacker"));

        if (badges.LastPlace?.PlayerId == playerId)
            result.Add(new PlayerBadgeInfo("bi bi-arrow-down", "bg-dark", "Last Place - Lowest ELO"));

        if (badges.TableDiver?.PlayerId == playerId)
            result.Add(new PlayerBadgeInfo("bi bi-box-arrow-down", "bg-info", $"Table Diver - {badges.TableDiver.Value} under-table losses"));

        if (badges.Newcomers.Any(n => n.PlayerId == playerId))
            result.Add(new PlayerBadgeInfo("bi bi-stars", "bg-success", "Newcomer - Joined in last 7 days"));

        return result;
    }
}

/// <summary>
/// Represents display information for a player badge
/// </summary>
public record PlayerBadgeInfo(string IconClass, string CssClass, string Tooltip);
