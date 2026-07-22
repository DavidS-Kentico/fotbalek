using Fotbalek.Application.Common.Abstractions;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Seasons;

/// <summary>
/// Closes one season, idempotently: re-reads the season and re-checks ClosedAt == null under an
/// update lock before doing anything; a concurrent loser sees ClosedAt set and does nothing.
/// System action — no captain check (the lazy close is triggered by any member's page load).
/// </summary>
public sealed record CloseSeasonCommand(int SeasonId) : ICommand;

internal sealed class CloseSeasonCommandHandler(IAppDbContext db, IDbLocks dbLocks)
    : ICommandHandler<CloseSeasonCommand>
{
    public async Task<Result> Handle(CloseSeasonCommand command, CancellationToken cancellationToken)
    {
        await dbLocks.LockSeasonRowAsync(command.SeasonId, cancellationToken);
        var season = await db.Seasons.FirstOrDefaultAsync(s => s.Id == command.SeasonId, cancellationToken);
        if (season == null) return Result.Success();
        if (season.ClosedAt != null) return Result.Success(); // concurrent close won — nothing to do

        var now = DateTimeOffset.UtcNow;
        if (season.EndsAt == null || season.EndsAt > now) return Result.Success(); // not actually due

        await SeasonCloseProcedure.CloseAsync(db, season, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
