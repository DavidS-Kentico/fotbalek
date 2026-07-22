using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Contracts.Chat;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Chat;

/// <summary>Loads a team's latest visible message above the member's join floor (rail preview).</summary>
internal static class ChatThreadPreviewLoader
{
    public static async Task<ChatThreadPreview?> LoadAsync(
        IAppDbContext db, int teamId, DateTimeOffset joinedAt, CancellationToken cancellationToken)
    {
        var row = await db.ChatMessages.AsNoTracking()
            .Where(m => m.TeamId == teamId && !m.IsDeleted && m.CreatedAt >= joinedAt)
            .OrderByDescending(m => m.Id)
            .Select(m => new
            {
                m.Id,
                m.SenderUserId,
                m.Body,
                m.CreatedAt,
                SenderName = db.Players
                    .Where(p => p.TeamId == teamId && p.UserId == m.SenderUserId)
                    .Select(p => p.Name)
                    .FirstOrDefault(),
            })
            .FirstOrDefaultAsync(cancellationToken);
        return row == null
            ? null
            : new ChatThreadPreview(row.Id, row.SenderUserId, row.SenderName ?? "Unknown", row.Body, row.CreatedAt);
    }
}
