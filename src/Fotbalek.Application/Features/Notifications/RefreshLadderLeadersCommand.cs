using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Features.Seasons;
using Fotbalek.Application.Features.Stats;
using Fotbalek.SharedKernel;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fotbalek.Application.Features.Notifications;

/// <summary>
/// Re-derives the ladder snapshots for a team — all-time plus its active season, if it has one — and
/// <b>announces nothing anywhere</b> (AI/notifications.md §6.3).
/// <para>
/// It exists because a ladder can move with no match involved. All four ladders filter on
/// <c>Player.IsActive</c> in both scopes, so (de)activating a player can change a #1; and shrinking a
/// season's EndsAt unassigns its tail matches and replays the seasonal ladder, moving
/// <c>SeasonPlayer.Elo</c> with no match recorded. Without this refresh the snapshot would go stale
/// until the next match blamed an innocent match for the change.
/// </para>
/// <para>
/// Deliberately silent rather than announced: "you are now #1 because somebody left" is not a thing to
/// celebrate, and the honest notification for it does not exist. Kept as its own command so neither it
/// nor the aftermath ends up with a nullable MatchId and a bool doing two jobs.
/// </para>
/// </summary>
public sealed record RefreshLadderLeadersCommand(int TeamId) : ICommand;

/// <summary>Raised post-commit by (de)activation and by the season-EndsAt shrink; the bridge turns it
/// into a nested dispatch so the refresh gets its own transaction and lock (§6.3).</summary>
public sealed record LadderRefreshDueEvent(int TeamId) : INotification;

internal sealed class LadderRefreshBridge(ISender sender, ILogger<LadderRefreshBridge> logger)
    : INotificationHandler<LadderRefreshDueEvent>
{
    public async Task Handle(LadderRefreshDueEvent notification, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new RefreshLadderLeadersCommand(notification.TeamId), cancellationToken);
        if (result.IsFailure)
            logger.LogError(
                "Ladder refresh failed for team {TeamId}: {Error}", notification.TeamId, result.Error.Code);
    }
}

internal sealed class RefreshLadderLeadersCommandHandler(
    IAppDbContext db,
    IDbLocks dbLocks,
    StatsEngine statsEngine,
    LadderLeaderSync ladderSync)
    : ICommandHandler<RefreshLadderLeadersCommand>
{
    public async Task<Result> Handle(RefreshLadderLeadersCommand command, CancellationToken cancellationToken)
    {
        await dbLocks.AcquireTeamTimelineLockAsync(command.TeamId, cancellationToken);

        var (playersById, matches) = await statsEngine.LoadAsync(command.TeamId);

        await ladderSync.SyncAllTimeAsync(
            command.TeamId, announceMatchId: null, matches, playersById, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var activeSeasonId = await db.Seasons
            .AsNoTracking()
            .Where(s => s.TeamId == command.TeamId)
            .Where(SeasonRules.ActiveAt(now))
            .Select(s => (int?)s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (activeSeasonId is int seasonId)
        {
            await ladderSync.SyncSeasonAsync(
                command.TeamId, seasonId, announceMatchId: null, matches, playersById, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
