using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Contracts.Chat;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Chat;

/// <summary>Refreshes one team's rail preview — used when the shown last message is deleted
/// and the previous one must surface. Null for non-members or an empty thread.</summary>
public sealed record GetChatThreadPreviewQuery(int TeamId) : IQuery<ChatThreadPreview?>;

internal sealed class GetChatThreadPreviewQueryHandler(IAppDbContext db, IUserContext userContext)
    : IQueryHandler<GetChatThreadPreviewQuery, ChatThreadPreview?>
{
    public async Task<Result<ChatThreadPreview?>> Handle(GetChatThreadPreviewQuery query, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not int userId)
            return Result.Success<ChatThreadPreview?>(null);

        var membership = await db.TeamMemberships.AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == userId && m.TeamId == query.TeamId, cancellationToken);
        if (membership == null)
            return Result.Success<ChatThreadPreview?>(null);

        return await ChatThreadPreviewLoader.LoadAsync(db, query.TeamId, membership.JoinedAt, cancellationToken);
    }
}
