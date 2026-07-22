using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Chat;

/// <summary>
/// Every member's read watermark for a team (userId → highest read message id), powering the
/// "seen by" readout on the caller's own messages. Members who have never opened the chat have
/// no row and are simply absent (effective watermark 0). Empty for non-members.
/// </summary>
public sealed record GetChatReadWatermarksQuery(int TeamId) : IQuery<Dictionary<int, int>>;

internal sealed class GetChatReadWatermarksQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<GetChatReadWatermarksQuery, Dictionary<int, int>>
{
    public async Task<Result<Dictionary<int, int>>> Handle(GetChatReadWatermarksQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberAsync(query.TeamId, cancellationToken))
            return new Dictionary<int, int>();
        return await db.ChatReadStates.AsNoTracking()
            .Where(r => r.TeamId == query.TeamId)
            .ToDictionaryAsync(r => r.UserId, r => r.LastReadMessageId, cancellationToken);
    }
}
