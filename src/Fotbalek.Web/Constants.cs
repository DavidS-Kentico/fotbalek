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
        public const int MinGamesForVarietyBadge = 10;
    }

    /// <summary>
    /// Season-related thresholds and enum-like values
    /// </summary>
    public static class Seasons
    {
        /// <summary>A season generates awards only when it has at least this many matches in total.</summary>
        public const int MinMatchesForAwards = 10;

        /// <summary>The Top-3-players award category requires at least this many matches played in the season.</summary>
        public const int MinMatchesForPlayerAward = 10;

        /// <summary>Values of <see cref="Data.Entities.SeasonAward.Category"/>.</summary>
        public static class AwardCategories
        {
            public const string Player = "Player";
            public const string Goalkeeper = "Goalkeeper";
            public const string Attacker = "Attacker";
            public const string Pair = "Pair";
        }
    }

    /// <summary>
    /// Pagination defaults
    /// </summary>
    public static class Pagination
    {
        public const int DefaultPageSize = 20;
        public const int DashboardRecentMatches = 8;
        public const int DashboardTopPlayers = 10;
        public const int PlayerDetailRecentMatches = 6;
    }
}
