using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Seasons;

/// <summary>Manual / premature end: sets EndsAt to now (if unset or in the future) and closes the season. Captain only.</summary>
public sealed record EndSeasonNowCommand(int SeasonId) : ICommand;

internal sealed class EndSeasonNowCommandHandler(
    IAppDbContext db, TeamAccess teamAccess, IDbLocks dbLocks, INotificationWriter notifications)
    : ICommandHandler<EndSeasonNowCommand>
{
    public async Task<Result> Handle(EndSeasonNowCommand command, CancellationToken cancellationToken)
    {
        var probe = await db.Seasons.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == command.SeasonId, cancellationToken);
        if (probe is null)
            return Result.Failure(Error.NotFound("Seasons.NotFound", "Season not found."));
        if (!await teamAccess.IsCaptainAsync(probe.TeamId, cancellationToken))
            return Result.Failure(CommonErrors.NotCaptain);

        await dbLocks.LockSeasonRowAsync(command.SeasonId, cancellationToken);
        var season = await db.Seasons.FirstOrDefaultAsync(s => s.Id == command.SeasonId, cancellationToken);
        if (season is null)
            return Result.Failure(Error.NotFound("Seasons.NotFound", "Season not found."));
        if (season.ClosedAt != null)
            return Result.Failure(Error.Conflict("Seasons.AlreadyClosed", "The season is already closed."));

        var now = DateTimeOffset.UtcNow;
        if (season.StartsAt > now)
            return Result.Failure(Error.Conflict(
                "Seasons.NotStarted", "A scheduled season has not started yet — delete it instead of ending it."));

        if (season.EndsAt == null || season.EndsAt > now)
        {
            season.EndsAt = now;
        }
        // The same notification write as the lazy close — this is the second CloseAsync call site, and
        // skipping it here would make a deliberately ended season finish in silence (§5.5).
        var frozen = await SeasonCloseProcedure.CloseAsync(db, season, now, cancellationToken);
        await SeasonNotifications.WriteCloseAsync(db, notifications, season, frozen, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
