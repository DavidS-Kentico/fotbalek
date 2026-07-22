using Fotbalek.Application.Common.Abstractions;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Matches;

/// <summary>Shared match rules used by the deletability queries and the delete command.</summary>
internal static class MatchRules
{
    /// <summary>Rule-only deletability: time window, closed season, no later matches for participants.</summary>
    public static async Task<(bool CanDelete, string? Reason)> CanDeleteWithReasonAsync(
        IAppDbContext db, int matchId, CancellationToken cancellationToken)
    {
        var match = await db.Matches
            .AsNoTracking()
            .Include(m => m.MatchPlayers)
            .FirstOrDefaultAsync(m => m.Id == matchId, cancellationToken);
        if (match == null) return (false, "Match not found");

        var hoursSinceCreation = (DateTimeOffset.UtcNow - match.CreatedAt).TotalHours;
        if (hoursSinceCreation > Constants.TimeThresholds.MatchDeletionWindowHours)
            return (false, $"Matches can only be deleted within {Constants.TimeThresholds.MatchDeletionWindowHours} hours of creation");

        // Matches of a closed season cannot be deleted — deleting would corrupt frozen standings
        // and awards. Reachable when the captain ends a season prematurely inside the 24h window.
        if (match.SeasonId != null &&
            await db.Seasons.AnyAsync(s => s.Id == match.SeasonId && s.ClosedAt != null, cancellationToken))
            return (false, "This match belongs to a closed season — its results are frozen");

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
                return (false, "Cannot delete: one or more players have played matches after this one");
        }

        return (true, null);
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
