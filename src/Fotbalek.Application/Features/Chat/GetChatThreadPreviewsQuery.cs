using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Contracts.Chat;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Chat;

/// <summary>
/// The latest visible message in every team where the CURRENT USER has claimed a Player — powers
/// the dock rail's last-message preview. One seek per team on the (TeamId, Id) index; the user's
/// team count is small. Teams with no messages above the join floor are simply absent.
/// </summary>
public sealed record GetChatThreadPreviewsQuery : IQuery<Dictionary<int, ChatThreadPreview>>;

internal sealed class GetChatThreadPreviewsQueryHandler(IAppDbContext db, IUserContext userContext)
    : IQueryHandler<GetChatThreadPreviewsQuery, Dictionary<int, ChatThreadPreview>>
{
    public async Task<Result<Dictionary<int, ChatThreadPreview>>> Handle(GetChatThreadPreviewsQuery query, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not int userId)
            return new Dictionary<int, ChatThreadPreview>();

        var memberships = await db.TeamMemberships.AsNoTracking()
            .Where(m => m.UserId == userId && m.Team.Players.Any(p => p.UserId == userId))
            .Select(m => new { m.TeamId, m.JoinedAt })
            .ToListAsync(cancellationToken);

        var result = new Dictionary<int, ChatThreadPreview>();
        foreach (var membership in memberships)
        {
            var preview = await ChatThreadPreviewLoader.LoadAsync(db, membership.TeamId, membership.JoinedAt, cancellationToken);
            if (preview != null)
                result[membership.TeamId] = preview;
        }
        return result;
    }
}
