namespace Fotbalek.Web;

/// <summary>
/// Application-wide constants
/// </summary>
public static class Constants
{
    /// <summary>
    /// Player positions in a foosball match
    /// </summary>
    public static class Positions
    {
        public const string Goalkeeper = "Goalkeeper";
        public const string Attacker = "Attacker";
    }

    /// <summary>
    /// ELO rating system constants
    /// </summary>
    public static class Elo
    {
        public const int DefaultRating = 1000;
        public const int MinimumRating = 100;
        public const int KFactor = 32;
    }

    /// <summary>
    /// Time-based thresholds
    /// </summary>
    public static class TimeThresholds
    {
        public const int ShareTokenExpirationHours = 24;
        public const int MatchDeletionWindowHours = 24;
        public const int RecentActivityDays = 7;
        public const int MinGamesForPartnerStats = 3;
        public const int MinGamesForPositionBadge = 5;
        public const int MinGamesForTomkoBadge = 7;
        public const int MinGamesForCarriedBadge = 10;
    }

    /// <summary>
    /// Pagination defaults
    /// </summary>
    public static class Pagination
    {
        public const int DefaultPageSize = 20;
        public const int DashboardRecentMatches = 7;
        public const int DashboardTopPlayers = 10;
        public const int PlayerDetailRecentMatches = 6;
    }
}
