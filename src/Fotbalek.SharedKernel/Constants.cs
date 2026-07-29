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
    /// Qualifying thresholds for the themed stats. They live here rather than inside the stat
    /// implementations because the UI quotes them back in each stat's description — one source of
    /// truth keeps the rule and the wording from drifting apart.
    /// </summary>
    public static class Stats
    {
        /// <summary>How far below their peak ELO a player must be to count as fallen.</summary>
        public const int MinDropFromPeak = 50;

        /// <summary>Minimum one-goal games before a close-game win rate is ranked.</summary>
        public const int MinCloseGames = 5;

        /// <summary>Minimum games together before a duo is ranked.</summary>
        public const int MinPairGames = 5;

        /// <summary>Minimum head-to-head games before a rivalry is ranked.</summary>
        public const int MinHeadToHeadGames = 4;

        /// <summary>Minimum consecutive results before a streak is reported.</summary>
        public const int MinStreak = 3;

        /// <summary>Combined-ELO gap that makes a team favourites (or underdogs).</summary>
        public const int UnderdogEloGap = 100;

        /// <summary>Goal margin at which a win counts as a demolition.</summary>
        public const int DominantWinMargin = 7;

        /// <summary>How many times the weaker partner's ELO the carrier must have for a win to count as a carry.</summary>
        public const double CarryEloMultiplier = 1.2;
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
    /// Notification feed limits and milestone thresholds
    /// </summary>
    public static class Notifications
    {
        /// <summary>Feed page / "load more". The same number as
        /// <see cref="Pagination.DefaultPageSize"/>, kept as its own constant for the same reason
        /// <see cref="Chat.HistoryPageSize"/> is: a feed page is tuned against its own surface.
        /// The feed fetches PageSize + 1 rows to detect has-more.</summary>
        public const int PageSize = 20;

        /// <summary>Unseen counts above this render as "99+", like chat's.</summary>
        public const int BadgeCap = 99;

        /// <summary>What a category resolves to when the user has never touched it (no stored row).</summary>
        public const NotificationChannel DefaultChannels = NotificationChannel.InApp;

        /// <summary>Win-streak lengths worth telling someone about, before the repeat step kicks in.</summary>
        public static readonly int[] WinStreakThresholds = [3, 5, 10];

        /// <summary>Past the last explicit threshold, every multiple of this counts.</summary>
        public const int WinStreakRepeatEvery = 5;

        /// <summary>Matches-played milestones, before the repeat step kicks in.</summary>
        public static readonly int[] MatchMilestones = [10, 25, 50, 100];

        /// <summary>Past the last explicit milestone, every multiple of this counts.</summary>
        public const int MatchMilestoneRepeatEvery = 100;
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
