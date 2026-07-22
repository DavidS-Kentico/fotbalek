using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Chat;

/// <summary>Author-only soft delete: tombstones the message, empties the body and drops its reactions.</summary>
public sealed record DeleteChatMessageCommand(int TeamId, int MessageId) : ICommand;

internal sealed class DeleteChatMessageCommandHandler(
    IAppDbContext db,
    IUserContext userContext,
    TeamAccess teamAccess,
    IEventCollector events)
    : ICommandHandler<DeleteChatMessageCommand>
{
    public async Task<Result> Handle(DeleteChatMessageCommand command, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not int userId)
            return Result.Failure(CommonErrors.NotAuthenticated);
        if (!await teamAccess.IsMemberAsync(command.TeamId, cancellationToken))
            return Result.Failure(CommonErrors.NotMember);

        var message = await db.ChatMessages
            .FirstOrDefaultAsync(m => m.Id == command.MessageId && m.TeamId == command.TeamId, cancellationToken);
        if (message == null || message.SenderUserId != userId || message.IsDeleted)
            return Result.Failure(Error.Conflict("Chat.DeleteFailed", "The message could not be deleted."));

        message.IsDeleted = true;
        message.Body = string.Empty;
        await db.ChatMessageReactions
            .Where(r => r.MessageId == command.MessageId)
            .ExecuteDeleteAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        events.Enqueue(new ChatMessageDeletedEvent(command.TeamId, command.MessageId));
        return Result.Success();
    }
}
