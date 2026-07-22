using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Seasons;

/// <summary>
/// Edit EndsAt of a non-closed season. Shrinking past already-assigned matches unassigns the tail
/// (requires <paramref name="AllowUnassign"/> — the UI confirms first) and replays the season
/// ladder. Extending an ended-pending-close season revives it. Runs under the team timeline lock
/// plus the season update lock; rejected if the lazy close won the race.
/// </summary>
public sealed record UpdateSeasonEndsAtCommand(int SeasonId, DateTimeOffset? NewEndsAt, bool AllowUnassign) : ICommand;

internal sealed class UpdateSeasonEndsAtCommandHandler(IAppDbContext db, TeamAccess teamAccess, IDbLocks dbLocks)
    : ICommandHandler<UpdateSeasonEndsAtCommand>
{
    public async Task<Result> Handle(UpdateSeasonEndsAtCommand command, CancellationToken cancellationToken)
    {
        var probe = await db.Seasons.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == command.SeasonId, cancellationToken);
        if (probe is null)
            return Result.Failure(Error.NotFound("Seasons.NotFound", "Season not found."));
        if (!await teamAccess.IsCaptainAsync(probe.TeamId, cancellationToken))
            return Result.Failure(CommonErrors.NotCaptain);

        await dbLocks.AcquireTeamTimelineLockAsync(probe.TeamId, cancellationToken);
        await dbLocks.LockSeasonRowAsync(command.SeasonId, cancellationToken);

        // Re-read under the lock — guarantees committed values.
        var season = await db.Seasons.FirstOrDefaultAsync(s => s.Id == command.SeasonId, cancellationToken);
        if (season is null)
            return Result.Failure(Error.NotFound("Seasons.NotFound", "Season not found."));
        if (season.ClosedAt != null)
            return Result.Failure(Error.Conflict(
                "Seasons.AlreadyClosed", "The season is already closed — its results are frozen."));
        if (command.NewEndsAt != null && command.NewEndsAt <= season.StartsAt)
            return Result.Failure(Error.Validation(
                "Seasons.InvalidPeriod", "The end date must be after the start date."));

        if (await SeasonRules.CheckNoOverlapAsync(db, season.TeamId, command.SeasonId, season.StartsAt, command.NewEndsAt, cancellationToken) is { } overlap)
            return Result.Failure(overlap);

        var seasonMatches = await db.Matches
            .Include(m => m.MatchPlayers)
            .Where(m => m.SeasonId == command.SeasonId)
            .ToListAsync(cancellationToken);
        var tail = command.NewEndsAt == null
            ? []
            : seasonMatches.Where(m => m.PlayedAt >= command.NewEndsAt).ToList();

        if (tail.Count > 0)
        {
            if (!command.AllowUnassign)
                return Result.Failure(Error.Conflict(
                    "Seasons.UnassignConfirmationRequired",
                    $"{tail.Count} match(es) were played after the new end date and would become off-season. Confirmation required."));

            foreach (var match in tail)
            {
                match.SeasonId = null;
                foreach (var mp in match.MatchPlayers)
                {
                    mp.SeasonEloBefore = null;
                    mp.SeasonEloAfter = null;
                    mp.SeasonEloChange = null;
                }
            }

            // Replay the whole season ladder from the remaining matches — the simple,
            // always-correct rollback of the unassigned tail's seasonal ELO.
            var remaining = seasonMatches.Except(tail).ToList();
            var existingLadder = await db.SeasonPlayers
                .Where(sp => sp.SeasonId == command.SeasonId)
                .ToListAsync(cancellationToken);
            SeasonLadderReplay.Replay(db, command.SeasonId, existingLadder, remaining);
        }

        season.EndsAt = command.NewEndsAt;
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
