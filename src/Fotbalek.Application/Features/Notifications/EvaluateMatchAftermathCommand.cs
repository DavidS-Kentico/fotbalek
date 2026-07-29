using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Features.Stats;
using Fotbalek.SharedKernel;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Fotbalek.Application.Features.Notifications;

/// <summary>
/// Everything a recorded (or deleted) match implies beyond the match itself: the four ladder leads in
/// both scopes and the four personal milestones (AI/notifications.md §6).
/// <para>
/// <paramref name="SeasonId"/> rides the command rather than being read off the match, <b>because the
/// handler must not load the match row to learn it</b> — on the delete path that row no longer
/// exists. It is also what keeps the delete path honest for a season whose EndsAt has passed but which
/// is not yet closed: an "active season" probe would skip it, while the deleted match's own SeasonId
/// names it exactly.
/// </para>
/// <para>
/// <paramref name="Notify"/> means "may announce at all", not "which scope": the announce scope is
/// derived from <paramref name="SeasonId"/> per §6.2 and the other scope stays silent regardless. So
/// <c>Notify: false</c> (the delete path) refreshes both scopes and tells nobody.
/// </para>
/// <para>
/// System action with no actor check, the same documented stance as CloseSeasonCommand: it is
/// dispatched by a post-commit bridge on behalf of a write that was already authorized, and it takes
/// no input a caller could steer (§11).
/// </para>
/// </summary>
public sealed record EvaluateMatchAftermathCommand(int TeamId, int MatchId, int? SeasonId, bool Notify) : ICommand;

/// <summary>
/// Raised post-commit by match create and match delete. The bridge below turns it into a nested
/// dispatch, which is what gets the aftermath its OWN transaction — it needs one for the team
/// timeline lock, and it must not run inside the match transaction, which holds the season row lock
/// while the ladders aggregate the team's whole history (§6.3).
/// </summary>
public sealed record MatchAftermathDueEvent(int TeamId, int MatchId, int? SeasonId, bool Notify) : INotification;

/// <summary>
/// Lives in Application beside the command it dispatches, exactly where the precedent lives
/// (SeasonCreatedPastDueEventHandler, in CreateSeasonCommand.cs) — nothing about the dispatch touches
/// a Web concern.
/// </summary>
internal sealed class MatchAftermathBridge(ISender sender, ILogger<MatchAftermathBridge> logger)
    : INotificationHandler<MatchAftermathDueEvent>
{
    public async Task Handle(MatchAftermathDueEvent notification, CancellationToken cancellationToken)
    {
        // Published post-commit, so the triggering transaction is finished: TransactionBehavior
        // commits and THEN drains the collector, and HasActiveTransaction reads
        // Database.CurrentTransaction, which EF clears on commit. This dispatch therefore sees no
        // ambient transaction and opens its own — which is what IDbLocks needs.
        var result = await sender.Send(
            new EvaluateMatchAftermathCommand(
                notification.TeamId, notification.MatchId, notification.SeasonId, notification.Notify),
            cancellationToken);

        if (result.IsFailure)
            logger.LogError(
                "Match aftermath failed for match {MatchId}: {Error}", notification.MatchId, result.Error.Code);
    }
}

internal sealed class EvaluateMatchAftermathCommandHandler(
    IAppDbContext db,
    IDbLocks dbLocks,
    StatsEngine statsEngine,
    LadderLeaderSync ladderSync,
    INotificationWriter writer)
    : ICommandHandler<EvaluateMatchAftermathCommand>
{
    public async Task<Result> Handle(EvaluateMatchAftermathCommand command, CancellationToken cancellationToken)
    {
        // Two matches recorded seconds apart would otherwise evaluate concurrently and write
        // contradictory snapshots. The lock already exists for exactly this per-team serialization.
        await dbLocks.AcquireTeamTimelineLockAsync(command.TeamId, cancellationToken);

        // ONE load, both scopes: the team's players (for IsActive) and its matches with their
        // MatchPlayers, which is where the all-time ladders and every milestone come from. The
        // seasonal aggregates come from filtering these same matches rather than a second query.
        var (playersById, matches) = await statsEngine.LoadAsync(command.TeamId);

        // All-time: refreshed always, announced only when the match was off-season (§6.2).
        await ladderSync.SyncAllTimeAsync(
            command.TeamId,
            command.Notify && command.SeasonId == null ? command.MatchId : null,
            matches,
            playersById,
            cancellationToken);

        if (command.SeasonId is int seasonId)
        {
            await ladderSync.SyncSeasonAsync(
                command.TeamId,
                seasonId,
                command.Notify ? command.MatchId : null,
                matches,
                playersById,
                cancellationToken);
        }

        // Milestones are all-time and only ever announced. On the delete path the match is gone from
        // the history, which is also exactly when there is nothing to celebrate.
        if (command.Notify && matches.FirstOrDefault(m => m.Id == command.MatchId) is { } match)
        {
            await MatchMilestones.WriteAsync(
                writer, command.TeamId, match, matches, playersById, cancellationToken);
        }

        // §4.1: the sync and the milestones only track entities, and AddAsync has already enqueued
        // their delivery events — every path that reaches them owes this save.
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
