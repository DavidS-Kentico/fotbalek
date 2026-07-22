using Fotbalek.Application.Common.Abstractions;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Chat;

/// <summary>
/// Per-team unread counts for all teams where the CURRENT USER has claimed a Player, in one
/// query: messages after both the join floor (CreatedAt ≥ JoinedAt) and the read watermark
/// (Id &gt; LastReadMessageId, 0 when no row yet), excluding tombstones.
/// </summary>
public sealed record GetChatUnreadCountsQuery : IQuery<Dictionary<int, int>>;

internal sealed class GetChatUnreadCountsQueryHandler(IAppDbContext db, IUserContext userContext)
    : IQueryHandler<GetChatUnreadCountsQuery, Dictionary<int, int>>
{
    public async Task<Result<Dictionary<int, int>>> Handle(GetChatUnreadCountsQuery query, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not int userId)
            return new Dictionary<int, int>();

        var rows = await db.TeamMemberships.AsNoTracking()
            .Where(m => m.UserId == userId && m.Team.Players.Any(p => p.UserId == userId))
            .Select(m => new
            {
                m.TeamId,
                Count = db.ChatMessages.Count(c =>
                    c.TeamId == m.TeamId
                    && !c.IsDeleted
                    && c.CreatedAt >= m.JoinedAt
                    && c.Id > (db.ChatReadStates
                        .Where(r => r.UserId == userId && r.TeamId == m.TeamId)
                        .Select(r => (int?)r.LastReadMessageId)
                        .FirstOrDefault() ?? 0)),
            })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.TeamId, r => r.Count);
    }
}
