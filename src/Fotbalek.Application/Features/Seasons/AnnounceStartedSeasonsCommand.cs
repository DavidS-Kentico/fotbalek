using Fotbalek.Application.Common.Abstractions;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Seasons;

/// <summary>
/// Announces the team's seasons that have started but were never announced. Dispatched from the same
/// lazy team-page hook as the lazy close, because <b>nothing runs at StartsAt</b> — the app has no
/// background service or scheduler (AI/notifications.md §5.4).
/// <para>
/// Idempotent and concurrency-safe: it re-checks under the season row lock, so concurrent page loads
/// cannot double-announce, and the check does not lean on a unique-index violation as flow control.
/// </para>
/// <para>
/// ⚠ <b>The lazy announce collides with the lazy close, and the close must win.</b> A season can run
/// its whole course between two visits — scheduled, started, ended, all while nobody opened a team
/// page. The hook runs the close loop FIRST and the <c>ClosedAt == null</c> re-check here then
/// suppresses the start announcement, so only "ended" is delivered. That leaves the column unstamped,
/// which is harmless precisely because the lookup filters on <c>ClosedAt == null</c> too — otherwise
/// the suppressed season would be re-dispatched on every page load forever.
/// </para>
/// <para>
/// System action with no captain check, like the lazy close it rides along with (§11).
/// </para>
/// </summary>
public sealed record AnnounceStartedSeasonsCommand(int TeamId) : ICommand;

internal sealed class AnnounceStartedSeasonsCommandHandler(
    IAppDbContext db, IDbLocks dbLocks, INotificationWriter notifications)
    : ICommandHandler<AnnounceStartedSeasonsCommand>
{
    public async Task<Result> Handle(AnnounceStartedSeasonsCommand command, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        var candidateIds = await db.Seasons
            .AsNoTracking()
            .Where(s => s.TeamId == command.TeamId
                && s.StartAnnouncedAt == null
                && s.StartsAt <= now
                && s.ClosedAt == null)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);
        if (candidateIds.Count == 0)
            return Result.Success();

        foreach (var seasonId in candidateIds)
        {
            await dbLocks.LockSeasonRowAsync(seasonId, cancellationToken);
            var season = await db.Seasons.FirstOrDefaultAsync(s => s.Id == seasonId, cancellationToken);
            if (season is null)
                continue;
            // Re-read under the lock: a concurrent page load may have announced it, and the close loop
            // that ran before this dispatch may have closed it.
            if (season.StartAnnouncedAt != null || season.StartsAt > now || season.ClosedAt != null)
                continue;

            season.StartAnnouncedAt = now;
            await SeasonNotifications.WriteStartedAsync(db, notifications, season, actorUserId: null, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
