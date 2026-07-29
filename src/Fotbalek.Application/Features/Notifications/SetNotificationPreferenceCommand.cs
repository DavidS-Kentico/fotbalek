using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Domain.Entities;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Notifications;

/// <summary>
/// Turns one category on or off for one team. Owner-scoped — it takes no user id, so changing
/// someone else's preferences is not expressible — and it re-verifies that the caller has a claimed
/// player in the target team: a preference row for a team you are not in is meaningless and would
/// leak team existence by id probing (AI/notifications.md §11).
/// </summary>
public sealed record SetNotificationPreferenceCommand(
    int TeamId, NotificationCategory Category, bool InAppEnabled) : ICommand;

internal sealed class SetNotificationPreferenceCommandHandler(IAppDbContext db, IUserContext userContext)
    : ICommandHandler<SetNotificationPreferenceCommand>
{
    public async Task<Result> Handle(SetNotificationPreferenceCommand command, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not int userId)
            return Result.Failure(CommonErrors.NotAuthenticated);

        var hasClaimedPlayer = await db.Players
            .AnyAsync(p => p.TeamId == command.TeamId && p.UserId == userId, cancellationToken);
        if (!hasClaimedPlayer)
            return Result.Failure(CommonErrors.NotMember);

        var row = await db.NotificationPreferences.FirstOrDefaultAsync(
            p => p.UserId == userId && p.TeamId == command.TeamId && p.Category == command.Category,
            cancellationToken);

        // Only the InApp bit is written; the reserved Push bit is preserved so phase 2 can share the
        // row (§8.2). Setting a category back to its default value stores an explicit row rather
        // than deleting one — a row is the record of an explicit choice, and it is what the future
        // global tier will need in order to override correctly (§8.4).
        var channels = Toggle(row?.Channels ?? Constants.Notifications.DefaultChannels, command.InAppEnabled);
        var now = DateTimeOffset.UtcNow;

        if (row != null)
        {
            row.Channels = channels;
            row.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }

        var created = new NotificationPreference
        {
            UserId = userId,
            TeamId = command.TeamId,
            Category = command.Category,
            Channels = channels,
            UpdatedAt = now,
        };
        db.NotificationPreferences.Add(created);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Unique-index race (two tabs flipping the same switch): the other write inserted
            // first — retry as an update, mirroring ChatReadStateAdvancer's recovery.
            db.Entry(created).State = EntityState.Detached;
            await db.NotificationPreferences
                .Where(p => p.UserId == userId && p.TeamId == command.TeamId && p.Category == command.Category)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Channels, channels)
                    .SetProperty(p => p.UpdatedAt, now),
                    cancellationToken);
        }

        return Result.Success();
    }

    private static NotificationChannel Toggle(NotificationChannel current, bool inAppEnabled) =>
        inAppEnabled
            ? current | NotificationChannel.InApp
            : current & ~NotificationChannel.InApp;
}
