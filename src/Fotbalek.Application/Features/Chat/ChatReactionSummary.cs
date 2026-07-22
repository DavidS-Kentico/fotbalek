using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Contracts.Chat;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Chat;

/// <summary>Loads a message's reaction chips (grouped by emoji, first-seen order).</summary>
internal static class ChatReactionSummary
{
    public static async Task<List<ChatReactionDto>> LoadAsync(
        IAppDbContext db, int messageId, CancellationToken cancellationToken)
    {
        var rows = await db.ChatMessageReactions.AsNoTracking()
            .Where(r => r.MessageId == messageId)
            .OrderBy(r => r.Id)
            .Select(r => new { r.Id, r.Emoji, r.UserId })
            .ToListAsync(cancellationToken);
        return rows
            .GroupBy(r => r.Emoji)
            .OrderBy(g => g.Min(x => x.Id))
            .Select(g => new ChatReactionDto(g.Key, g.Select(x => x.UserId).ToList()))
            .ToList();
    }
}
