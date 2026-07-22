namespace Fotbalek.SharedKernel;

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

        /// <summary>Values of the SeasonAward entity's Category property.</summary>
        public static class AwardCategories
        {
            public const string Player = "Player";
            public const string Goalkeeper = "Goalkeeper";
            public const string Attacker = "Attacker";
            public const string Pair = "Pair";
        }
    }

    /// <summary>
    /// Team chat limits and tunables
    /// </summary>
    public static class Chat
    {
        /// <summary>Server-clamped maximum message length.</summary>
        public const int MaxMessageLength = 2000;

        /// <summary>Messages per history page (initial load and scroll-back).</summary>
        public const int HistoryPageSize = 50;

        /// <summary>In-memory soft send limit: at most this many messages…</summary>
        public const int SendThrottleMaxMessages = 5;

        /// <summary>…per this many seconds, per user.</summary>
        public const int SendThrottleWindowSeconds = 5;

        /// <summary>The composer refreshes its "typing" signal at most this often.</summary>
        public const int TypingRefreshSeconds = 3;

        /// <summary>Server auto-clears a typing entry after this long without a refresh
        /// (guards against a dropped "stopped" signal).</summary>
        public const int TypingExpirySeconds = 6;

        /// <summary>Allows ZWJ / skin-tone emoji sequences.</summary>
        public const int MaxReactionEmojiLength = 32;

        /// <summary>In-app banner body preview truncation.</summary>
        public const int BannerPreviewLength = 120;

        /// <summary>In-app banner auto-dismiss (manual dismiss also available).</summary>
        public const int BannerAutoDismissSeconds = 6;
    }

    /// <summary>
    /// Pagination defaults
    /// </summary>
    public static class Pagination
    {
        public const int DefaultPageSize = 20;
        // Dashboard Recent Matches: initial load and per-"Load more" batch size (loaded from
        // the server on demand, so the feed keeps offering more until the season/team runs out).
        public const int DashboardRecentMatches = 10;
        public const int DashboardTopPlayers = 10;
        public const int PlayerDetailRecentMatches = 6;
    }
}
