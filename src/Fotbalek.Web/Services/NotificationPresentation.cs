using Fotbalek.Contracts.Notifications;
using Fotbalek.SharedKernel;

namespace Fotbalek.Web.Services;

/// <summary>
/// What one feed row renders: its icon (used only when the row has no actor face), its headline, and an
/// optional second line of context. <c>Detail</c> deliberately never repeats the team name — the row
/// renders that as a chip, and only when the user has more than one team (AI/notifications.md §9.1).
/// </summary>
public sealed record NotificationPresentation(string Icon, string Title, string? Detail);

/// <summary>Where clicking a row goes. Chat rows open the dock instead of navigating, because the
/// conversation is a panel and not a page.</summary>
public abstract record NotificationTarget
{
    /// <summary>Navigate to a page.</summary>
    public sealed record Url(string Href) : NotificationTarget;

    /// <summary>Open the chat dock on this team (AI/notifications.md §5.2).</summary>
    public sealed record OpenChat(int TeamId) : NotificationTarget;
}

/// <summary>Label + hint for one preference category on the settings panel.</summary>
public sealed record NotificationCategoryPresentation(string Icon, string Label, string Hint);

/// <summary>
/// The presentation side of notifications, following <c>StatDisplay</c>: all English copy, all icons
/// and all routes live here and nowhere else — Application and the entities know only the enums, the
/// ids and the numbers (repo convention, commit f62ff6f "no ui texts in app layer";
/// AI/notifications.md §3.2, §8.1, §10).
/// <para>
/// Every switch here is exhaustive with no fallback arm on purpose: adding a
/// <see cref="NotificationType"/> or <see cref="NotificationCategory"/> without its wording and target
/// is a build error (CS8509, promoted in Directory.Build.props), not a row that renders as its own
/// enum name at runtime.
/// </para>
/// <para>
/// This file lands under <c>Web/Services</c>, which Tailwind's <c>@source</c> glob already scans, so
/// the CSS classes named below do get generated — the same way StatPresentation's do.
/// </para>
/// </summary>
public static class NotificationDisplay
{
#pragma warning disable CS8524 // Only the named members exist; an out-of-range cast is a bug worth throwing on.
    /// <summary>How to render this row.</summary>
    public static NotificationPresentation Describe(NotificationDto n) => n.Type switch
    {
        NotificationType.MatchRecorded => new(
            "bi bi-controller",
            $"{Actor(n)} recorded a match with you",
            null),

        NotificationType.ChatMention => new(
            "bi bi-at",
            $"{Actor(n)} mentioned you",
            null),

        NotificationType.ChatReaction => new(
            "bi bi-emoji-smile",
            $"{Actor(n)} reacted {n.Emoji} to your message",
            null),

        NotificationType.SeasonStarted => new(
            "bi bi-flag-fill",
            $"{Season(n)} has started",
            null),

        NotificationType.SeasonEnded => new(
            "bi bi-flag-fill",
            // Null covers both "you did not play" and "you were inactive at close" — the same wording
            // for both is the honest reading: there is no final rank either way (§5.5).
            n.Value is int rank
                ? $"{Season(n)} ended — you finished #{rank}"
                : $"{Season(n)} ended",
            null),

        NotificationType.SeasonAward => new(
            "bi bi-award-fill",
            $"You won {Ordinal(n.Value)} place: {AwardCategoryLabel(n.Category)}",
            $"{Season(n)}{PartnerSuffix(n)}"),

        NotificationType.LadderLeadTaken => new(
            "bi bi-trophy-fill",
            LeadTakenTitle(n),
            SeasonScope(n)),

        NotificationType.LadderLeadLost => new(
            "bi bi-arrow-down-circle",
            $"{Subject(n)} took the #1 {LadderNoun(n.Category)} spot from you",
            SeasonScope(n)),

        NotificationType.PeakElo => new(
            "bi bi-graph-up-arrow",
            $"New personal best: {n.Value} ELO",
            null),

        NotificationType.WinStreak => new(
            "bi bi-fire",
            $"{n.Value} wins in a row",
            null),

        NotificationType.MatchMilestone => new(
            "bi bi-flag",
            $"That was your {Ordinal(n.Value)} match",
            null),

        NotificationType.NemesisBeaten => new(
            "bi bi-shield-fill-check",
            $"You finally beat {Subject(n)}",
            null),
    };

    /// <summary>Where clicking this row goes.</summary>
    public static NotificationTarget Target(NotificationDto n) => n.Type switch
    {
        // Jumping to the specific message is not supported: the conversation paginates from the
        // newest message and has no seek-to-id path (chat.md §4.7). The dock opens on the right team.
        NotificationType.ChatMention => new NotificationTarget.OpenChat(n.TeamId),
        NotificationType.ChatReaction => new NotificationTarget.OpenChat(n.TeamId),

        NotificationType.MatchRecorded => Page(n, n.MatchId is int id ? $"matches/{id}" : null),

        NotificationType.SeasonStarted => Page(n, SeasonPath(n)),
        NotificationType.SeasonEnded => Page(n, SeasonPath(n)),
        NotificationType.SeasonAward => Page(n, SeasonPath(n)),

        // A seasonal lead points at the season's own tables; an all-time one at Rankings.
        NotificationType.LadderLeadTaken => Page(n, SeasonPath(n) ?? "rankings"),
        NotificationType.LadderLeadLost => Page(n, SeasonPath(n) ?? "rankings"),

        // The milestones are numbers that live on the player's own page.
        NotificationType.PeakElo => Page(n, PlayerPath(n)),
        NotificationType.WinStreak => Page(n, PlayerPath(n)),
        NotificationType.MatchMilestone => Page(n, PlayerPath(n)),
        NotificationType.NemesisBeaten => Page(n, PlayerPath(n)),
    };

    /// <summary>The settings panel's label and hint for one preference category.</summary>
    public static NotificationCategoryPresentation Describe(NotificationCategory category) => category switch
    {
        NotificationCategory.Matches => new(
            "bi bi-controller", "Matches", "When someone records a match you played in"),
        NotificationCategory.Chat => new(
            "bi bi-chat-dots", "Chat", "Mentions and reactions to your messages"),
        NotificationCategory.Seasons => new(
            "bi bi-flag", "Seasons", "Season starts, endings, your result and awards"),
        NotificationCategory.Rankings => new(
            "bi bi-trophy", "Rankings", "When the #1 spots change"),
        NotificationCategory.Milestones => new(
            "bi bi-stars", "Milestones", "Personal bests, streaks and match milestones"),
    };
#pragma warning restore CS8524

    // ── Wording helpers ───────────────────────────────────────────────────────────────────────

    private static string Actor(NotificationDto n) => n.ActorName ?? "Someone";

    private static string Subject(NotificationDto n) => n.SubjectName ?? "Someone";

    private static string Season(NotificationDto n) => n.SeasonName ?? "The season";

    private static string LeadTakenTitle(NotificationDto n) => n.Category switch
    {
        Constants.Seasons.AwardCategories.Goalkeeper => "You're the #1 goalkeeper",
        Constants.Seasons.AwardCategories.Attacker => "You're the #1 attacker",
        Constants.Seasons.AwardCategories.Pair => n.SubjectName is { } partner
            ? $"You and {partner} are the #1 duo"
            : "You're in the #1 duo",
        _ => "You're #1 in the team",
    };

    private static string LadderNoun(string? category) => category switch
    {
        Constants.Seasons.AwardCategories.Goalkeeper => "goalkeeper",
        Constants.Seasons.AwardCategories.Attacker => "attacker",
        Constants.Seasons.AwardCategories.Pair => "duo",
        _ => "player",
    };

    private static string AwardCategoryLabel(string? category) => category switch
    {
        Constants.Seasons.AwardCategories.Goalkeeper => "Goalkeeper of the season",
        Constants.Seasons.AwardCategories.Attacker => "Attacker of the season",
        Constants.Seasons.AwardCategories.Pair => "Duo of the season",
        _ => "Player of the season",
    };

    /// <summary>The season name for a seasonal ladder row; nothing for an all-time one, whose scope is
    /// the team the chip already names.</summary>
    private static string? SeasonScope(NotificationDto n) =>
        n.SeasonId != null ? Season(n) : null;

    private static string PartnerSuffix(NotificationDto n) =>
        n.SubjectName is { } partner ? $" · with {partner}" : string.Empty;

    private static string Ordinal(int? value) => value switch
    {
        null => "—",
        // 11th/12th/13th are the exceptions the naive rule gets wrong.
        var v when v % 100 is >= 11 and <= 13 => $"{v}th",
        var v when v % 10 == 1 => $"{v}st",
        var v when v % 10 == 2 => $"{v}nd",
        var v when v % 10 == 3 => $"{v}rd",
        var v => $"{v}th",
    };

    // ── Target helpers ────────────────────────────────────────────────────────────────────────

    private static NotificationTarget Page(NotificationDto n, string? relativePath) =>
        new NotificationTarget.Url(
            relativePath is null
                ? $"/team/{n.TeamCodeName}"
                : $"/team/{n.TeamCodeName}/{relativePath}");

    private static string? SeasonPath(NotificationDto n) =>
        n.SeasonId is int id ? $"seasons/{id}" : null;

    /// <summary>The recipient's own player page, which is where these numbers live. Falls back to the
    /// team dashboard in the state that should not happen — a milestone for someone with no claimed
    /// player, who could not have received it (§1).</summary>
    private static string? PlayerPath(NotificationDto n) =>
        n.RecipientPlayerId is int id ? $"players/{id}" : null;
}
