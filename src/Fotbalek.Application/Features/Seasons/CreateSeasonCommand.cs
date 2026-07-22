using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Contracts.Seasons;
using Fotbalek.Domain.Entities;
using Fotbalek.SharedKernel;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Seasons;

/// <summary>
/// Creates a season (captain only), optionally importing existing off-season matches whose
/// PlayedAt falls inside the period — the seasonal ladder is replayed from them.
/// Serialized through the per-team timeline lock (creation has no Season row to lock yet).
/// </summary>
public sealed record CreateSeasonCommand(
    int TeamId,
    string Name,
    string? Description,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    IReadOnlyCollection<int>? ImportMatchIds = null) : ICommand<SeasonDto>;

/// <summary>
/// Raised when a season was created already past its end date — the post-commit handler closes
/// it on the spot (results and awards generated immediately) instead of waiting for the lazy close.
/// </summary>
public sealed record SeasonCreatedPastDueEvent(int SeasonId) : INotification;

internal sealed class SeasonCreatedPastDueEventHandler(ISender sender) : INotificationHandler<SeasonCreatedPastDueEvent>
{
    public async Task Handle(SeasonCreatedPastDueEvent notification, CancellationToken cancellationToken)
    {
        // Published post-commit, so the create transaction is finished — this dispatch opens
        // its own transaction through the normal pipeline. CloseSeasonCommand is idempotent.
        await sender.Send(new CloseSeasonCommand(notification.SeasonId), cancellationToken);
    }
}

internal sealed class CreateSeasonCommandHandler(
    IAppDbContext db,
    TeamAccess teamAccess,
    IDbLocks dbLocks,
    IEventCollector events)
    : ICommandHandler<CreateSeasonCommand, SeasonDto>
{
    public async Task<Result<SeasonDto>> Handle(CreateSeasonCommand command, CancellationToken cancellationToken)
    {
        var name = command.Name;
        var description = command.Description;
        if (SeasonRules.ValidateNameAndDescription(ref name, ref description) is { } invalid)
            return Result.Failure<SeasonDto>(invalid);
        if (command.EndsAt != null && command.EndsAt <= command.StartsAt)
            return Result.Failure<SeasonDto>(Error.Validation(
                "Seasons.InvalidPeriod", "The end date must be after the start date."));

        if (!await teamAccess.IsCaptainAsync(command.TeamId, cancellationToken))
            return Result.Failure<SeasonDto>(CommonErrors.NotCaptain);

        // Creation has no Season row to lock yet, so the overlap and name checks are
        // serialized through a per-team application lock instead (re-validated under it).
        await dbLocks.AcquireTeamTimelineLockAsync(command.TeamId, cancellationToken);
        if (await SeasonRules.CheckNameAvailableAsync(db, command.TeamId, name, null, cancellationToken) is { } nameTaken)
            return Result.Failure<SeasonDto>(nameTaken);
        if (await SeasonRules.CheckNoOverlapAsync(db, command.TeamId, null, command.StartsAt, command.EndsAt, cancellationToken) is { } overlap)
            return Result.Failure<SeasonDto>(overlap);

        var season = new Season
        {
            TeamId = command.TeamId,
            Name = name,
            Description = description,
            StartsAt = command.StartsAt,
            EndsAt = command.EndsAt,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Seasons.Add(season);
        await db.SaveChangesAsync(cancellationToken);

        if (command.ImportMatchIds is { Count: > 0 })
        {
            var matches = await db.Matches
                .Include(m => m.MatchPlayers)
                .Where(m => command.ImportMatchIds.Contains(m.Id))
                .ToListAsync(cancellationToken);

            foreach (var match in matches)
            {
                if (match.TeamId != command.TeamId)
                    return Result.Failure<SeasonDto>(Error.Validation(
                        "Seasons.ImportWrongTeam", "Only matches of this team can be imported."));
                if (match.SeasonId != null)
                    return Result.Failure<SeasonDto>(Error.Validation(
                        "Seasons.ImportAssigned", "Only unassigned matches can be imported."));
                if (match.PlayedAt < command.StartsAt || (command.EndsAt != null && match.PlayedAt >= command.EndsAt))
                    return Result.Failure<SeasonDto>(Error.Validation(
                        "Seasons.ImportOutsidePeriod", "Only matches within the season period can be imported."));
                match.SeasonId = season.Id;
            }

            // Replay in chronological order to build the seasonal ladder; all-time ELO is untouched.
            SeasonLadderReplay.Replay(db, season.Id, existingLadder: [], matches);
            await db.SaveChangesAsync(cancellationToken);
        }

        // A season created entirely in the past is already due — close it immediately after
        // this command's transaction commits (see SeasonCreatedPastDueEvent).
        if (season.EndsAt != null && season.EndsAt <= DateTimeOffset.UtcNow)
        {
            events.Enqueue(new SeasonCreatedPastDueEvent(season.Id));
        }

        return season.ToDto();
    }
}
