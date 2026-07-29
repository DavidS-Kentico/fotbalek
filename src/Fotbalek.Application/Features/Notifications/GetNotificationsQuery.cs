using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Contracts.Notifications;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Notifications;

/// <summary>
/// One page of the current user's feed, newest first. <b>Account-scoped and cross-team</b>: it takes
/// no team parameter, so being rendered inside one team's navbar does not narrow it — a mention from
/// another team appears in the same list, wearing that team's chip (AI/notifications.md §1, §9.3).
/// <para>
/// <paramref name="BeforeId"/> is a cursor over the monotonic Id, so "load more" is a keyset page on
/// the (UserId, Id DESC) index rather than an offset. Callers ask for PageSize + 1 rows and use the
/// extra one as the has-more flag (§9.1).
/// </para>
/// </summary>
public sealed record GetNotificationsQuery(int? BeforeId, int Take) : IQuery<List<NotificationDto>>;

internal sealed class GetNotificationsQueryHandler(IAppDbContext db, IUserContext userContext)
    : IQueryHandler<GetNotificationsQuery, List<NotificationDto>>
{
    public async Task<Result<List<NotificationDto>>> Handle(
        GetNotificationsQuery query, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not int userId)
            return new List<NotificationDto>();

        var take = Math.Clamp(query.Take, 1, Constants.Notifications.PageSize + 1);

        return await db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId)
            .Where(n => query.BeforeId == null || n.Id < query.BeforeId)
            .OrderByDescending(n => n.Id)
            .Take(take)
            // Actor display name and avatar come from the actor's claimed Player in this team,
            // resolved at READ time — the same rule as chat and the live game.
            .Select(n => new NotificationDto(
                n.Id,
                n.TeamId,
                n.Team.Name,
                n.Team.CodeName,
                n.Team.Players
                    .Where(p => p.UserId == userId)
                    .Select(p => (int?)p.Id)
                    .FirstOrDefault(),
                n.Type,
                n.CreatedAt,
                n.SeenAt,
                n.ReadAt,
                n.ActorPlayerId,
                n.ActorPlayer == null ? null : n.ActorPlayer.Name,
                n.ActorPlayer == null ? null : (int?)n.ActorPlayer.AvatarId,
                n.SubjectPlayerId,
                n.SubjectPlayer == null ? null : n.SubjectPlayer.Name,
                n.MatchId,
                n.SeasonId,
                n.Season == null ? null : n.Season.Name,
                n.ChatMessageId,
                n.Category,
                n.Value,
                n.Emoji))
            .ToListAsync(cancellationToken);
    }
}
