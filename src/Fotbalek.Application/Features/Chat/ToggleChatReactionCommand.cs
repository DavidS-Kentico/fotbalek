using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Domain.Entities;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Chat;

/// <summary>Toggles the user's reaction (unique per user+emoji+message → idempotent) and
/// broadcasts the updated summary. Reacting to a tombstone is rejected.</summary>
public sealed record ToggleChatReactionCommand(int TeamId, int MessageId, string Emoji) : ICommand;

internal sealed class ToggleChatReactionCommandHandler(
    IAppDbContext db,
    IUserContext userContext,
    TeamAccess teamAccess,
    IEventCollector events,
    INotificationWriter notifications)
    : ICommandHandler<ToggleChatReactionCommand>
{
    public async Task<Result> Handle(ToggleChatReactionCommand command, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not int userId)
            return Result.Failure(CommonErrors.NotAuthenticated);

        var emoji = (command.Emoji ?? string.Empty).Trim();
        if (emoji.Length == 0 || emoji.Length > Constants.Chat.MaxReactionEmojiLength)
            return Result.Failure(Error.Validation("Chat.InvalidEmoji", "Invalid reaction."));

        if (!await teamAccess.IsMemberAsync(command.TeamId, cancellationToken))
            return Result.Failure(CommonErrors.NotMember);

        var message = await db.ChatMessages.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == command.MessageId && m.TeamId == command.TeamId, cancellationToken);
        if (message == null || message.IsDeleted)
            return Result.Failure(Error.Conflict("Chat.ReactionFailed", "The message cannot be reacted to."));

        var existing = await db.ChatMessageReactions
            .FirstOrDefaultAsync(r => r.MessageId == command.MessageId && r.UserId == userId && r.Emoji == emoji, cancellationToken);
        // Which half of the toggle this is, captured before the save — afterwards it is not
        // recoverable, and only the ADD half notifies (AI/notifications.md §5.3).
        var added = existing == null;
        var reaction = existing;
        if (existing != null)
        {
            db.ChatMessageReactions.Remove(existing);
        }
        else
        {
            reaction = new ChatMessageReaction
            {
                MessageId = command.MessageId,
                UserId = userId,
                Emoji = emoji,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.ChatMessageReactions.Add(reaction);
        }

        var saveFailed = false;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Unique-index race (two tabs toggling at once) — the other toggle won;
            // fall through and broadcast the current state. Detach the loser so a later save on
            // this scope's context cannot replay it (the ChatReadStateAdvancer precedent).
            saveFailed = true;
            db.Entry(reaction!).State = EntityState.Detached;
        }

        var summary = await ChatReactionSummary.LoadAsync(db, command.MessageId, cancellationToken);
        events.Enqueue(new ChatReactionChangedEvent(command.TeamId, command.MessageId, summary));

        // Deliberately after the try/catch, and guarded on all three conditions. Writing before the
        // save would put the notification rows into a save that can throw: on the race path nothing
        // is inserted, the catch swallows it, there is no later SaveChanges — and the row would be
        // silently dropped while its delivery event still flushed (§4.1, §5.3). The race path is also
        // exactly the case where the reaction already existed, so no notification was warranted.
        if (added && !saveFailed && message.SenderUserId != userId)
        {
            var actorPlayerId = await db.Players
                .AsNoTracking()
                .Where(p => p.TeamId == command.TeamId && p.UserId == userId)
                .Select(p => (int?)p.Id)
                .FirstOrDefaultAsync(cancellationToken);

            await notifications.AddAsync(
                new NotificationDraft(
                    NotificationType.ChatReaction, command.TeamId,
                    // The emoji is part of the key, so toggling the same one off and on again does
                    // not re-notify. Removing a reaction never deletes the row — you were told a
                    // true thing at the time.
                    $"reaction:{command.MessageId}:{userId}:{emoji}")
                {
                    ActorUserId = userId,
                    ActorPlayerId = actorPlayerId,
                    ChatMessageId = command.MessageId,
                    Emoji = emoji,
                },
                [message.SenderUserId],
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }
}
