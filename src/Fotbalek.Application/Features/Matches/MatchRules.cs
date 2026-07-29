using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Contracts.Matches;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Matches;

/// <summary>Shared match rules used by the deletability queries and the delete command.</summary>
internal static class MatchRules
{
    /// <summary>
    /// Rule-only deletability: time window, closed season, no later matches for participants.
    /// Returns the blocker, or <see cref="MatchDeletionBlocker.None"/> when the delete may proceed.
    /// </summary>
    public static async Task<MatchDeletionBlocker> DeletionBlockerAsync(
        IAppDbContext db, int matchId, CancellationToken cancellationToken)
    {
        var match = await db.Matches
            .AsNoTracking()
            .Include(m => m.MatchPlayers)
            .FirstOrDefaultAsync(m => m.Id == matchId, cancellationToken);
        if (match == null) return MatchDeletionBlocker.NotFound;

        var hoursSinceCreation = (DateTimeOffset.UtcNow - match.CreatedAt).TotalHours;
        if (hoursSinceCreation > Constants.TimeThresholds.MatchDeletionWindowHours)
            return MatchDeletionBlocker.DeletionWindowElapsed;

        // Matches of a closed season cannot be deleted — deleting would corrupt frozen standings
        // and awards. Reachable when the captain ends a season prematurely inside the 24h window.
        if (match.SeasonId != null &&
            await db.Seasons.AnyAsync(s => s.Id == match.SeasonId && s.ClosedAt != null, cancellationToken))
            return MatchDeletionBlocker.SeasonClosed;

        // Check if this is the most recent match for all players involved.
        // This ensures ELO reversal won't corrupt subsequent match history.
        // We use MatchId for comparison since matches are always created with current time.
        foreach (var mp in match.MatchPlayers)
        {
            var hasLaterMatch = await db.MatchPlayers.AnyAsync(laterMp =>
                laterMp.PlayerId == mp.PlayerId &&
                laterMp.MatchId > match.Id,
                cancellationToken);

            if (hasLaterMatch)
                return MatchDeletionBlocker.LaterMatchPlayed;
        }

        return MatchDeletionBlocker.None;
    }

    /// <summary>Actor rule: team captain OR has a Player participating in the match.</summary>
    public static async Task<bool> IsCaptainOrParticipantAsync(
        IAppDbContext db, int matchId, int teamId, int userId, CancellationToken cancellationToken)
    {
        var team = await db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == teamId, cancellationToken);
        if (team == null) return false;
        if (team.CaptainUserId == userId) return true;

        return await db.MatchPlayers
            .AsNoTracking()
            .AnyAsync(mp => mp.MatchId == matchId && mp.Player.UserId == userId, cancellationToken);
    }
}
