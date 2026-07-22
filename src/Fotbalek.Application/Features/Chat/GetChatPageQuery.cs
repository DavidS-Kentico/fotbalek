using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Contracts.Chat;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Chat;

/// <summary>
/// One page of history, newest-first internally but returned oldest-first for display.
/// Pass BeforeId when scrolling back; the join floor caps how far back a member can read.
/// Null for non-members.
/// </summary>
public sealed record GetChatPageQuery(int TeamId, int JoinFloorId, int? BeforeId = null)
    : IQuery<List<ChatMessageDto>?>;

internal sealed class GetChatPageQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<GetChatPageQuery, List<ChatMessageDto>?>
{
    public async Task<Result<List<ChatMessageDto>?>> Handle(GetChatPageQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberAsync(query.TeamId, cancellationToken))
            return Result.Success<List<ChatMessageDto>?>(null);

        var messages = db.ChatMessages.AsNoTracking()
            .Where(m => m.TeamId == query.TeamId && m.Id > query.JoinFloorId);
        if (query.BeforeId is int b)
            messages = messages.Where(m => m.Id < b);

        var rows = await messages
            .OrderByDescending(m => m.Id)
            .Take(Constants.Chat.HistoryPageSize)
            .Select(m => new
            {
                m.Id,
                m.SenderUserId,
                m.Body,
                m.CreatedAt,
                m.IsDeleted,
                m.EditedAt,
                SenderPlayer = db.Players
                    .Where(p => p.TeamId == query.TeamId && p.UserId == m.SenderUserId)
                    .Select(p => new { p.Name, p.AvatarId })
                    .FirstOrDefault(),
                SenderUserName = m.Sender.UserName,
                Reactions = m.Reactions
                    .OrderBy(r => r.Id)
                    .Select(r => new { r.Id, r.Emoji, r.UserId })
                    .ToList(),
            })
            .ToListAsync(cancellationToken);
        rows.Reverse();

        return rows.Select(r => new ChatMessageDto(
                r.Id,
                r.SenderUserId,
                r.SenderPlayer?.Name ?? r.SenderUserName ?? "Unknown",
                r.SenderPlayer?.AvatarId ?? 1,
                r.IsDeleted ? string.Empty : r.Body,
                r.CreatedAt,
                r.IsDeleted,
                r.EditedAt,
                r.Reactions
                    .GroupBy(x => x.Emoji)
                    .OrderBy(g => g.Min(x => x.Id))
                    .Select(g => new ChatReactionDto(g.Key, g.Select(x => x.UserId).ToList()))
                    .ToList()))
            .ToList();
    }
}
