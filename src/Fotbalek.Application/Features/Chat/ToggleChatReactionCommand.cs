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
    IEventCollector events)
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
        if (existing != null)
            db.ChatMessageReactions.Remove(existing);
        else
            db.ChatMessageReactions.Add(new ChatMessageReaction
            {
                MessageId = command.MessageId,
                UserId = userId,
                Emoji = emoji,
                CreatedAt = DateTimeOffset.UtcNow,
            });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Unique-index race (two tabs toggling at once) — the other toggle won;
            // fall through and broadcast the current state.
        }

        var summary = await ChatReactionSummary.LoadAsync(db, command.MessageId, cancellationToken);
        events.Enqueue(new ChatReactionChangedEvent(command.TeamId, command.MessageId, summary));
        return Result.Success();
    }
}
