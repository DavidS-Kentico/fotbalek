using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Contracts.Notifications;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Notifications;

/// <summary>
/// The current user's per-team preferences, one entry per team where they have a CLAIMED player,
/// with all five categories materialised from the stored rows plus the defaults — the settings UI
/// never has to know that storage is sparse (AI/notifications.md §8.4).
/// <para>
/// Owner-scoped: it takes no user id. Note that a membership is not the rule here — a team where the
/// user has not claimed a player cannot produce notifications at all, so it is not listed.
/// </para>
/// </summary>
public sealed record GetNotificationPreferencesQuery : IQuery<List<TeamNotificationPreferencesDto>>;

internal sealed class GetNotificationPreferencesQueryHandler(IAppDbContext db, IUserContext userContext)
    : IQueryHandler<GetNotificationPreferencesQuery, List<TeamNotificationPreferencesDto>>
{
    public async Task<Result<List<TeamNotificationPreferencesDto>>> Handle(
        GetNotificationPreferencesQuery query, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not int userId)
            return new List<TeamNotificationPreferencesDto>();

        var teams = await db.TeamMemberships
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.Team.Players.Any(p => p.UserId == userId))
            .OrderBy(m => m.JoinedAt)
            .Select(m => new { m.TeamId, m.Team.Name, m.Team.CodeName })
            .ToListAsync(cancellationToken);
        if (teams.Count == 0)
            return new List<TeamNotificationPreferencesDto>();

        // Only overrides exist, and v1 never writes the reserved global (TeamId == null) tier.
        var stored = await db.NotificationPreferences
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.TeamId != null)
            .Select(p => new { TeamId = p.TeamId!.Value, p.Category, p.Channels })
            .ToListAsync(cancellationToken);

        var storedByKey = stored.ToDictionary(p => (p.TeamId, p.Category), p => p.Channels);

        return teams
            .Select(team => new TeamNotificationPreferencesDto(
                team.TeamId,
                team.Name,
                team.CodeName,
                Enum.GetValues<NotificationCategory>()
                    .Select(category =>
                    {
                        var channels = storedByKey.TryGetValue((team.TeamId, category), out var overridden)
                            ? overridden
                            : Constants.Notifications.DefaultChannels;
                        return new NotificationCategoryPreferenceDto(
                            category, (channels & NotificationChannel.InApp) != 0);
                    })
                    .ToList()))
            .ToList();
    }
}
