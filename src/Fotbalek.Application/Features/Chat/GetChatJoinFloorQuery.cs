using Fotbalek.Application.Common.Abstractions;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Chat;

/// <summary>
/// The member's history floor as a message id: the newest message older than their JoinedAt
/// (0 if none), so all subsequent queries are pure id comparisons. Computed once per panel
/// open — a single seek on the (TeamId, CreatedAt) index. Null for non-members.
/// </summary>
public sealed record GetChatJoinFloorQuery(int TeamId) : IQuery<int?>;

internal sealed class GetChatJoinFloorQueryHandler(IAppDbContext db, IUserContext userContext)
    : IQueryHandler<GetChatJoinFloorQuery, int?>
{
    public async Task<Result<int?>> Handle(GetChatJoinFloorQuery query, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not int userId)
            return Result.Success<int?>(null);

        var membership = await db.TeamMemberships.AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == userId && m.TeamId == query.TeamId, cancellationToken);
        if (membership == null)
            return Result.Success<int?>(null);

        var floor = await db.ChatMessages
            .Where(m => m.TeamId == query.TeamId && m.CreatedAt < membership.JoinedAt)
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => (int?)m.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return floor ?? 0;
    }
}
