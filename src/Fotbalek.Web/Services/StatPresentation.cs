using Fotbalek.Contracts.Stats;
using Fotbalek.SharedKernel;

namespace Fotbalek.Web.Services;

/// <summary>Inline-badge styling for the stats that opt into one.</summary>
public sealed record StatBadgeStyle(string IconClass, string CssClass);

/// <summary>
/// Everything the UI needs to render one stat: its wording, its emoji, its optional badge styling
/// and how to phrase a holder's value.
/// </summary>
public sealed record StatPresentation(
    string Name,
    string Emoji,
    string Description,
    StatBadgeStyle? Badge,
    Func<StatHolder, string> FormatValue);

/// <summary>One inline badge a player holds, formatted for rendering next to their name.</summary>
public sealed record PlayerBadge(string IconClass, string CssClass, string Tooltip);

/// <summary>
/// The presentation side of the stats engine: names, emoji, descriptions, badge styling and the
/// wording of a holder's value, one entry per <see cref="StatKey"/>. English copy and CSS classes
/// live here and nowhere else — Application returns keys, numbers and player ids only.
/// <para>
/// <see cref="Describe"/> is an exhaustive switch with no fallback arm on purpose: adding a
/// <see cref="StatKey"/> without its presentation is a build error (CS8509, promoted to an error in
/// Directory.Build.props), not a stat that silently renders as its own key at runtime.
/// </para>
/// <para>
/// Descriptions quote the qualifying thresholds straight from <see cref="Constants.Stats"/> /
/// <see cref="Constants.TimeThresholds"/>, so the wording cannot drift from the rule.
/// </para>
/// </summary>
public static class StatDisplay
{
    private static readonly IReadOnlyDictionary<StatKey, StatPresentation> Catalog =
        Enum.GetValues<StatKey>().ToDictionary(key => key, Describe);

    /// <summary>How to render the stat behind <paramref name="key"/>.</summary>
    public static StatPresentation For(StatKey key) => Catalog[key];

    /// <summary>How to render this result.</summary>
    public static StatPresentation Display(this StatResult result) => Catalog[result.Key];

#pragma warning disable CS8524 // Only the named members exist; an out-of-range cast is a bug worth throwing on.
    private static StatPresentation Describe(StatKey key) => key switch
    {
        // ---- Rankings ----
        StatKey.TopRated => new(
            "Top Rated", "⭐",
            "Player with the highest current ELO",
            new("bi bi-star-fill", "bg-warning text-gray-900"),
            h => $"{h.Value} ELO"),

        StatKey.LastPlace => new(
            "Last Place", "\U0001F4A8",
            "Player with the lowest current ELO",
            new("bi bi-arrow-down", "bg-gray-900"),
            h => $"{h.Value} ELO"),

        StatKey.TopGainer => new(
            "Top Gainer", "\U0001F4C8",
            "Best ELO gain in the period",
            null,
            h => $"+{h.Value} ELO"),

        StatKey.TopLoser => new(
            "Top Loser", "\U0001F4C9",
            "Worst ELO change in the period",
            null,
            h => $"{h.Value} ELO"),

        StatKey.BestWinRate => new(
            "Best Win%", "\U0001F4CA",
            $"Highest win rate (min {Constants.TimeThresholds.MinGamesForPositionBadge} games)",
            new("bi bi-percent", "bg-primary"),
            h => $"{h.Value}% ({Fraction(h)})"),

        // ---- Streaks ----
        StatKey.HotStreak => new(
            "Hot Streak", "\U0001F525",
            $"Longest active winning streak (min {Constants.Stats.MinStreak} wins)",
            new("bi bi-fire", "bg-danger"),
            h => $"{h.Value} wins in a row"),

        StatKey.ColdStreak => new(
            "Cold Streak", "❄",
            $"Longest active losing streak (min {Constants.Stats.MinStreak} losses)",
            new("bi bi-snow", "bg-info"),
            h => $"{h.Value} losses in a row"),

        StatKey.StreakKing => new(
            "Streak King", "\U0001F451",
            $"Longest winning streak in the period (min {Constants.Stats.MinStreak} wins)",
            new("bi bi-gem", "bg-primary"),
            h => $"{h.Value} wins in a row"),

        StatKey.SlumpKing => new(
            "Slump King", "\U0001F926",
            $"Longest losing streak in the period (min {Constants.Stats.MinStreak} losses)",
            new("bi bi-thermometer-snow", "bg-gray-900"),
            h => $"{h.Value} losses in a row"),

        // ---- Margins ----
        StatKey.Destroyer => new(
            "Destroyer", "\U0001F4A5",
            $"Most wins by a {Constants.Stats.DominantWinMargin}+ goal margin",
            new("bi bi-lightning-charge-fill", "bg-danger"),
            h => $"{h.Value} dominant wins"),

        StatKey.Lucker => new(
            "Lucker", "\U0001F340",
            "Most 1-10 losses (one goal scored)",
            new("bi bi-life-preserver", "bg-warning text-gray-900"),
            h => $"{h.Value} crushing defeats"),

        StatKey.TableDiver => new(
            "Table Diver", "\U0001F931",
            "Most 0-10 losses",
            new("bi bi-box-arrow-down", "bg-info"),
            h => $"{h.Value} under-table losses"),

        StatKey.TableSender => new(
            "Table Sender", "\U0001F4AA",
            "Most 10-0 wins",
            new("bi bi-box-arrow-up", "bg-brand"),
            h => $"{h.Value} table sends"),

        StatKey.CardiacKid => new(
            "Cardiac Kid", "\U0001F493",
            $"Best win rate in 1-goal games (min {Constants.Stats.MinCloseGames} such games)",
            null,
            h => $"{h.Value}% in close games ({Fraction(h)})"),

        // ---- ELO swings ----
        StatKey.BiggestEloWin => new(
            "Biggest Win", "\U0001F680",
            "Largest single-match ELO gain",
            null,
            h => $"+{h.Value} ELO in one match"),

        StatKey.BiggestEloLoss => new(
            "Biggest Loss", "\U0001F4C9",
            "Largest single-match ELO loss",
            null,
            h => $"{h.Value} ELO in one match"),

        // ---- Positions ----
        StatKey.BestAttacker => new(
            "Best ATK", "\U0001F3AF",
            $"Highest goals scored per match as ATK (min {Constants.TimeThresholds.MinGamesForPositionBadge} games)",
            new("bi bi-bullseye", "bg-danger"),
            h => $"{Average(h):F1} scored/game"),

        StatKey.BestGoalkeeper => new(
            "Best GK", "\U0001F92F",
            $"Lowest goals conceded per match as GK (min {Constants.TimeThresholds.MinGamesForPositionBadge} games)",
            new("bi bi-shield-fill", "bg-neutral"),
            h => $"{Average(h):F1} conceded/game"),

        // ---- Rivalries ----
        StatKey.Nemesis => new(
            "Nemesis", "\U0001F47F",
            $"Most lopsided losing record against a single opponent (min {Constants.Stats.MinHeadToHeadGames} games)",
            null,
            h => $"loses {Fraction(h)} vs {h.Detail}"),

        // ---- Partnerships ----
        StatKey.BestFriend => new(
            "Best Friend", "\U0001F46F",
            $"Highest win rate as a duo (min {Constants.Stats.MinPairGames} games together)",
            null,
            h => $"{h.Value}% with {h.Detail} ({Fraction(h)})"),

        StatKey.WorstFriend => new(
            "Toxic Duo", "☠",
            $"Lowest win rate as a duo (min {Constants.Stats.MinPairGames} games together)",
            null,
            h => $"{h.Value}% with {h.Detail} ({Fraction(h)})"),

        // ---- Underdog ----
        StatKey.GiantSlayer => new(
            "Giant Slayer", "\U0001F5E1",
            $"Most wins where your team was {Constants.Stats.UnderdogEloGap}+ ELO underdogs",
            null,
            h => $"{h.Value} upset wins"),

        StatKey.ChokeArtist => new(
            "Choke Artist", "\U0001F633",
            $"Most losses where your team was {Constants.Stats.UnderdogEloGap}+ ELO favorites",
            null,
            h => $"{h.Value} chokes"),

        // ---- Career arc ----
        StatKey.PeakElo => new(
            "Peak ELO", "\U0001F3D4",
            "Highest ELO ever reached",
            null,
            h => $"{h.Value} ELO peak"),

        StatKey.FurthestFromPeak => new(
            "Fallen Star", "\U0001F4C9",
            $"Currently furthest below their peak ELO (min {Constants.Stats.MinDropFromPeak} drop)",
            null,
            // Ratio is (current, peak) — the drop itself is the value.
            h => $"-{h.Value} from peak ({h.Ratio?.Whole} → {h.Ratio?.Part})"),

        // ---- Activity ----
        StatKey.VarietyPlayer => new(
            "Variety Player", "\U0001F308",
            $"Most even distribution of games across active teammates (min {Constants.TimeThresholds.MinGamesForVarietyBadge})",
            null,
            // Value is evenness in hundredths of a percent.
            h => $"{Math.Round(h.Value / 100.0)}% evenness"),

        // ---- Special ----
        StatKey.Carried => new(
            "Carried", "\U0001F91D",
            $"Wins where partner had {(Constants.Stats.CarryEloMultiplier - 1) * 100:0}%+ higher ELO and both opponents were weaker (min {Constants.TimeThresholds.MinGamesForCarriedBadge})",
            new("bi bi-people-fill", "bg-purple text-white"),
            h => $"{h.Value} carried wins"),

        StatKey.Newcomer => new(
            "Newcomer", "✨",
            $"Joined in the last {Constants.TimeThresholds.RecentActivityDays} days",
            new("bi bi-stars", "bg-brand"),
            _ => "joined recently"),

        StatKey.TomkoMemorial => new(
            "Tomko Memorial", "\U0001F3C6",
            $"Most games played in a single day (min {Constants.TimeThresholds.MinGamesForTomkoBadge})",
            new("bi bi-calendar-event", "bg-warning text-gray-900"),
            h => $"{h.Value} games in one day"),
    };

    /// <summary>The heading a theme's group of stats is filed under.</summary>
    public static string DisplayName(this StatTheme theme) => theme switch
    {
        StatTheme.Rankings => "Rankings",
        StatTheme.Streaks => "Streaks",
        StatTheme.Margins => "Margins",
        StatTheme.EloSwings => "ELO Swings",
        StatTheme.Positions => "Positions",
        StatTheme.Rivalries => "Rivalries",
        StatTheme.Partnerships => "Partnerships",
        StatTheme.Underdog => "Underdog",
        StatTheme.CareerArc => "Career Arc",
        StatTheme.Activity => "Activity",
        StatTheme.Special => "Special",
    };

    /// <summary>The icon shown beside a theme's heading.</summary>
    public static string Icon(this StatTheme theme) => theme switch
    {
        StatTheme.Rankings => "bi bi-bar-chart",
        StatTheme.Streaks => "bi bi-fire",
        StatTheme.Margins => "bi bi-arrow-down-up",
        StatTheme.EloSwings => "bi bi-graph-up",
        StatTheme.Positions => "bi bi-person-badge",
        StatTheme.Rivalries => "bi bi-shield-shaded",
        StatTheme.Partnerships => "bi bi-people",
        StatTheme.Underdog => "bi bi-trophy",
        StatTheme.CareerArc => "bi bi-graph-up-arrow",
        StatTheme.Activity => "bi bi-calendar-check",
        StatTheme.Special => "bi bi-star",
    };

    /// <summary>The role a player is fielded in most often, as shown on their profile.</summary>
    public static string PreferredPositionLabel(this PositionLean lean) => lean switch
    {
        PositionLean.Goalkeeper => "Goalkeeper",
        PositionLean.Attacker => "Attacker",
        PositionLean.Balanced => "Flexible",
        // With no games at all there is nothing to prefer — reads the same as an even split.
        PositionLean.Unknown => "Flexible",
    };

    /// <summary>The role a player performs better in, or null when there is not enough data to say.</summary>
    public static string? BetterPositionLabel(this PositionLean lean) => lean switch
    {
        PositionLean.Goalkeeper => "Goalkeeper",
        PositionLean.Attacker => "Attacker",
        PositionLean.Balanced => "Either",
        PositionLean.Unknown => null,
    };
#pragma warning restore CS8524

    /// <summary>Returns the badges a single player holds, formatted for inline rendering.</summary>
    public static List<PlayerBadge> PlayerBadges(IEnumerable<StatResult> results, int playerId) =>
        results
            .Select(r => new
            {
                Presentation = Catalog[r.Key],
                Holder = r.Holders.FirstOrDefault(h => h.PlayerId == playerId)
            })
            .Where(x => x.Presentation.Badge != null && x.Holder != null)
            .Select(x => new PlayerBadge(
                IconClass: x.Presentation.Badge!.IconClass,
                CssClass: x.Presentation.Badge.CssClass,
                Tooltip: $"{x.Presentation.Name} - {x.Presentation.FormatValue(x.Holder!)}"))
            .ToList();

    /// <summary>"7/10" — the operands behind a rate-style value.</summary>
    private static string Fraction(StatHolder holder) => $"{holder.Ratio?.Part}/{holder.Ratio?.Whole}";

    /// <summary>The per-game average a ratio represents, rounded by the caller's format string.</summary>
    private static double Average(StatHolder holder) =>
        holder.Ratio is { Whole: > 0 } ratio ? (double)ratio.Part / ratio.Whole : 0;
}
