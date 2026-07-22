using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Seasons;

/// <summary>
/// Deletes a season: matches become off-season (SeasonId and SeasonElo* cleared — the FK is
/// NO ACTION, so this is handler-managed), and all season rows including awards are removed
/// (players lose the achievements from that season). All-time ELO is unaffected. Captain only.
/// </summary>
public sealed record DeleteSeasonCommand(int SeasonId) : ICommand;

internal sealed class DeleteSeasonCommandHandler(IAppDbContext db, TeamAccess teamAccess, IDbLocks dbLocks)
    : ICommandHandler<DeleteSeasonCommand>
{
    public async Task<Result> Handle(DeleteSeasonCommand command, CancellationToken cancellationToken)
    {
        var probe = await db.Seasons.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == command.SeasonId, cancellationToken);
        if (probe is null)
            return Result.Failure(Error.NotFound("Seasons.NotFound", "Season not found."));
        if (!await teamAccess.IsCaptainAsync(probe.TeamId, cancellationToken))
            return Result.Failure(CommonErrors.NotCaptain);

        await dbLocks.LockSeasonRowAsync(command.SeasonId, cancellationToken);
        var season = await db.Seasons.FirstOrDefaultAsync(s => s.Id == command.SeasonId, cancellationToken);
        if (season == null) return Result.Success();

        await db.MatchPlayers
            .Where(mp => mp.Match.SeasonId == command.SeasonId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(mp => mp.SeasonEloBefore, (int?)null)
                .SetProperty(mp => mp.SeasonEloAfter, (int?)null)
                .SetProperty(mp => mp.SeasonEloChange, (int?)null),
                cancellationToken);

        await db.Matches
            .Where(m => m.SeasonId == command.SeasonId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.SeasonId, (int?)null), cancellationToken);

        // SeasonPlayer (with its SeasonPlayerResult), SeasonPair and SeasonAward rows cascade.
        db.Seasons.Remove(season);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
