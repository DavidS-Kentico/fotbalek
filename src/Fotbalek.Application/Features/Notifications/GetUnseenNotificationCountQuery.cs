using Fotbalek.Application.Common.Abstractions;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Notifications;

/// <summary>
/// The bell's badge: how many rows have arrived since the user last looked. One filtered-index
/// count, recomputed (never incremented) on every event — the same discipline as chat's unread
/// counts (AI/notifications.md §7.2). Account-scoped and cross-team, like the feed itself.
/// </summary>
public sealed record GetUnseenNotificationCountQuery : IQuery<int>;

internal sealed class GetUnseenNotificationCountQueryHandler(IAppDbContext db, IUserContext userContext)
    : IQueryHandler<GetUnseenNotificationCountQuery, int>
{
    public async Task<Result<int>> Handle(GetUnseenNotificationCountQuery query, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not int userId)
            return 0;

        return await db.Notifications
            .AsNoTracking()
            .CountAsync(n => n.UserId == userId && n.SeenAt == null, cancellationToken);
    }
}
