using Fotbalek.SharedKernel;

namespace Fotbalek.Contracts.Notifications;

/// <summary>One category's in-app switch, already resolved against the defaults.</summary>
public record NotificationCategoryPreferenceDto(NotificationCategory Category, bool InAppEnabled);

/// <summary>
/// One team's notification preferences, with all five categories materialised from the stored
/// (sparse) rows plus the defaults — the UI never has to know about sparseness
/// (AI/notifications.md §8.4). Only teams where the user has a CLAIMED player appear: a team
/// without one cannot produce notifications at all (§1).
/// </summary>
public record TeamNotificationPreferencesDto(
    int TeamId,
    string TeamName,
    string TeamCodeName,
    List<NotificationCategoryPreferenceDto> Categories);
