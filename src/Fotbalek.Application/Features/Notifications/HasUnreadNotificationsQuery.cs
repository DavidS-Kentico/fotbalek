using Fotbalek.Application.Common.Abstractions;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Notifications;

/// <summary>
/// Whether the user has ANY unread row, anywhere in their history — not just on the page the feed
/// has loaded. Drives the "Mark all as read" button's visibility, which must match the command's
/// reach: the command clears every unread row (AI/notifications.md §7.2), so hiding the button
/// because the visible page happens to be all-read would leave a backlog it could have cleared.
/// Account-scoped and cross-team, like the feed itself.
/// <para>
/// An EXISTS probe rather than a count — the button needs a yes/no, and the probe stops at the
/// first hit (unread rows cluster at the top of the (UserId, Id) index, newest first).
/// </para>
/// </summary>
public sealed record HasUnreadNotificationsQuery : IQuery<bool>;

internal sealed class HasUnreadNotificationsQueryHandler(IAppDbContext db, IUserContext userContext)
    : IQueryHandler<HasUnreadNotificationsQuery, bool>
{
    public async Task<Result<bool>> Handle(HasUnreadNotificationsQuery query, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not int userId)
            return false;

        return await db.Notifications
            .AsNoTracking()
            .AnyAsync(n => n.UserId == userId && n.ReadAt == null, cancellationToken);
    }
}
