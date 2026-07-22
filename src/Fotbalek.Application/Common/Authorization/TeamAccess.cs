using Fotbalek.Application.Common.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Common.Authorization;

/// <summary>
/// Membership/captaincy checks shared by handlers. Every handler re-verifies access per dispatch
/// (never per circuit) — which is also what a future API host needs (AI/architecture.md §3).
/// </summary>
public sealed class TeamAccess(IAppDbContext db, IUserContext userContext)
{
    /// <summary>The current user's id, or null when anonymous.</summary>
    public int? UserId => userContext.UserId;

    public async Task<bool> IsMemberAsync(int teamId, CancellationToken cancellationToken = default)
    {
        if (userContext.UserId is not int userId) return false;
        return await db.TeamMemberships.AnyAsync(
            m => m.UserId == userId && m.TeamId == teamId, cancellationToken);
    }

    public async Task<bool> IsCaptainAsync(int teamId, CancellationToken cancellationToken = default)
    {
        if (userContext.UserId is not int userId) return false;
        return await db.Teams.AnyAsync(
            t => t.Id == teamId && t.CaptainUserId == userId, cancellationToken);
    }

    // Id-resolving variants for handlers whose request carries no TeamId. A missing entity yields
    // false — the caller returns NotMember rather than confirming the id exists to a non-member.

    public async Task<bool> IsMemberOfPlayerTeamAsync(int playerId, CancellationToken cancellationToken = default)
    {
        if (userContext.UserId is not int userId) return false;
        return await db.Players.AnyAsync(
            p => p.Id == playerId && p.Team.Members.Any(m => m.UserId == userId), cancellationToken);
    }

    public async Task<bool> IsMemberOfSeasonTeamAsync(int seasonId, CancellationToken cancellationToken = default)
    {
        if (userContext.UserId is not int userId) return false;
        return await db.Seasons.AnyAsync(
            s => s.Id == seasonId && s.Team.Members.Any(m => m.UserId == userId), cancellationToken);
    }

    public async Task<bool> IsMemberOfMatchTeamAsync(int matchId, CancellationToken cancellationToken = default)
    {
        if (userContext.UserId is not int userId) return false;
        return await db.Matches.AnyAsync(
            m => m.Id == matchId && m.Team.Members.Any(mb => mb.UserId == userId), cancellationToken);
    }
}
