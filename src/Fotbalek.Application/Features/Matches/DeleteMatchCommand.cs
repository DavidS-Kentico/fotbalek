using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Matches;

/// <summary>
/// Deletes a match and reverses its ELO changes (all-time and seasonal). Permission and the
/// deletion rules are re-checked inside the transaction — the caller's button state may be stale.
/// </summary>
public sealed record DeleteMatchCommand(int TeamId, int MatchId) : ICommand;

internal sealed class DeleteMatchCommandHandler(
    IAppDbContext db,
    IUserContext userContext,
    IDbLocks dbLocks)
    : ICommandHandler<DeleteMatchCommand>
{
    private static readonly Error CannotDelete = Error.Conflict(
        "Matches.CannotDelete", "Cannot delete this match. It may have been modified or the deletion window has passed.");

    public async Task<Result> Handle(DeleteMatchCommand command, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not int userId)
            return Result.Failure(CommonErrors.NotAuthenticated);

        // Re-check the rules + actor permission right before deleting.
        var (canDelete, _) = await MatchRules.CanDeleteWithReasonAsync(db, command.MatchId, cancellationToken);
        if (!canDelete)
            return Result.Failure(CannotDelete);
        if (!await MatchRules.IsCaptainOrParticipantAsync(db, command.MatchId, command.TeamId, userId, cancellationToken))
            return Result.Failure(CannotDelete);

        var match = await db.Matches
            .Include(m => m.MatchPlayers)
            .FirstOrDefaultAsync(m => m.Id == command.MatchId, cancellationToken);
        if (match == null || match.TeamId != command.TeamId)
            return Result.Failure(CannotDelete);

        // For a seasonal match, re-verify under the season update lock — the check cannot race
        // a concurrent close, and a concurrent EndsAt shrink may have unassigned the match.
        if (match.SeasonId is int seasonId)
        {
            await dbLocks.LockSeasonRowAsync(seasonId, cancellationToken);
            await db.Entry(match).ReloadAsync(cancellationToken);
            if (match.SeasonId == seasonId)
            {
                var stillOpen = await db.Seasons.AnyAsync(
                    s => s.Id == seasonId && s.ClosedAt == null, cancellationToken);
                if (!stillOpen)
                    return Result.Failure(CannotDelete);
            }
        }

        // Reverse ELO changes
        foreach (var mp in match.MatchPlayers)
        {
            var player = await db.Players.FindAsync([mp.PlayerId], cancellationToken);
            if (player != null)
            {
                player.Elo = mp.EloBefore;
            }
        }

        // Seasonal ELO is reversed too, in the same transaction. The existing "no participant
        // has a later match" guard is a superset of the seasonal requirement, so no additional
        // ordering check is needed.
        if (match.SeasonId is int sid)
        {
            var participantIds = match.MatchPlayers.Select(mp => mp.PlayerId).ToList();
            var ladderRows = await db.SeasonPlayers
                .Where(sp => sp.SeasonId == sid && participantIds.Contains(sp.PlayerId))
                .ToListAsync(cancellationToken);

            foreach (var mp in match.MatchPlayers)
            {
                var ladderRow = ladderRows.FirstOrDefault(sp => sp.PlayerId == mp.PlayerId);
                if (ladderRow != null && mp.SeasonEloBefore is int seasonEloBefore)
                {
                    ladderRow.Elo = seasonEloBefore;
                }
            }

            // A SeasonPlayer left with zero season matches is deleted.
            foreach (var ladderRow in ladderRows)
            {
                var hasOtherSeasonMatch = await db.MatchPlayers.AnyAsync(mp =>
                    mp.PlayerId == ladderRow.PlayerId &&
                    mp.MatchId != match.Id &&
                    mp.Match.SeasonId == sid,
                    cancellationToken);
                if (!hasOtherSeasonMatch)
                {
                    db.SeasonPlayers.Remove(ladderRow);
                }
            }
        }

        db.Matches.Remove(match);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
