namespace Fotbalek.SharedKernel;

/// <summary>
/// Type → preference category. Lives in SharedKernel because both Application (the write-time
/// mute filter, AI/notifications.md §8.3) and Web (grouping the settings UI) need it, and because
/// it is a classification rather than presentation.
/// <para>
/// <see cref="Of"/> is an exhaustive switch with no fallback arm on purpose: adding a
/// <see cref="NotificationType"/> without classifying it is a build error (CS8509, promoted in
/// Directory.Build.props), not a row that silently escapes every preference toggle.
/// </para>
/// </summary>
public static class NotificationCategories
{
#pragma warning disable CS8524 // Only the named members exist; an out-of-range cast is a bug worth throwing on.
    public static NotificationCategory Of(NotificationType type) => type switch
    {
        NotificationType.MatchRecorded => NotificationCategory.Matches,

        NotificationType.ChatMention => NotificationCategory.Chat,
        NotificationType.ChatReaction => NotificationCategory.Chat,

        NotificationType.SeasonStarted => NotificationCategory.Seasons,
        NotificationType.SeasonEnded => NotificationCategory.Seasons,
        NotificationType.SeasonAward => NotificationCategory.Seasons,

        NotificationType.LadderLeadTaken => NotificationCategory.Rankings,
        NotificationType.LadderLeadLost => NotificationCategory.Rankings,

        NotificationType.PeakElo => NotificationCategory.Milestones,
        NotificationType.WinStreak => NotificationCategory.Milestones,
        NotificationType.MatchMilestone => NotificationCategory.Milestones,
        NotificationType.NemesisBeaten => NotificationCategory.Milestones,
    };
#pragma warning restore CS8524
}
