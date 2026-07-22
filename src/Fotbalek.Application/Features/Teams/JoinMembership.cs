using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Teams;

/// <summary>Shared membership upsert used by the join flows (password, share token).</summary>
internal static class JoinMembership
{
    public static async Task EnsureMemberAsync(IAppDbContext db, int userId, int teamId, CancellationToken cancellationToken)
    {
        var exists = await db.TeamMemberships
            .AnyAsync(m => m.UserId == userId && m.TeamId == teamId, cancellationToken);
        if (exists)
            return;

        var membership = new TeamMembership
        {
            UserId = userId,
            TeamId = teamId,
            JoinedAt = DateTimeOffset.UtcNow
        };
        db.TeamMemberships.Add(membership);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Unique-index race: another request created it; treat as joined.
            db.Entry(membership).State = EntityState.Detached;
            var raced = await db.TeamMemberships
                .AnyAsync(m => m.UserId == userId && m.TeamId == teamId, cancellationToken);
            if (!raced) throw;
        }
    }
}
