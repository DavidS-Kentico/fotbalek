using Fotbalek.Application.Common.Abstractions;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Notifications;

/// <summary>
/// Unseen counts broken down by team, for the per-team bell badge on the Home teams list — the one
/// nav-less page that matters, since the bell itself lives in TeamLayout (AI/notifications.md §9.4).
/// Deliberately the same UNSEEN tier as the bell's badge, so opening the bell once clears both
/// surfaces. Teams with nothing unseen are absent from the dictionary.
/// </summary>
public sealed record GetUnseenNotificationCountsByTeamQuery : IQuery<Dictionary<int, int>>;

internal sealed class GetUnseenNotificationCountsByTeamQueryHandler(IAppDbContext db, IUserContext userContext)
    : IQueryHandler<GetUnseenNotificationCountsByTeamQuery, Dictionary<int, int>>
{
    public async Task<Result<Dictionary<int, int>>> Handle(
        GetUnseenNotificationCountsByTeamQuery query, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not int userId)
            return new Dictionary<int, int>();

        var rows = await db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId && n.SeenAt == null)
            .GroupBy(n => n.TeamId)
            .Select(g => new { TeamId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.TeamId, r => r.Count);
    }
}
