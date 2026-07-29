using Fotbalek.Application.Common.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Notifications;

/// <summary>
/// Who can receive a team's notifications: users with a CLAIMED player in that team. Same rule as
/// the chat dock — a membership without a claimed player is a transient state, and every v1 trigger
/// is about a player anyway. A member who has not claimed one gets nothing until they do
/// (AI/notifications.md §1, §4.2).
/// <para>
/// Both queries run on Players alone: a claimed player IS the membership signal the rest of the app
/// uses. Excluding the actor is the WRITER's job (it owns the draft's ActorUserId), so these return
/// everyone and are safe to reuse for the actor-less system writes.
/// </para>
/// </summary>
internal static class NotificationRecipients
{
    /// <summary>The claimed users behind the given players of a team.</summary>
    public static async Task<List<int>> ForPlayersAsync(
        IAppDbContext db, int teamId, IEnumerable<int> playerIds, CancellationToken cancellationToken)
    {
        var ids = playerIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];

        return await db.Players
            .AsNoTracking()
            .Where(p => p.TeamId == teamId && ids.Contains(p.Id) && p.UserId != null)
            .Select(p => p.UserId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    /// <summary>Every user with a claimed player in the team — including deactivated players, who
    /// are still people on the team.</summary>
    public static Task<List<int>> ForTeamAsync(IAppDbContext db, int teamId, CancellationToken cancellationToken) =>
        db.Players
            .AsNoTracking()
            .Where(p => p.TeamId == teamId && p.UserId != null)
            .Select(p => p.UserId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
}
