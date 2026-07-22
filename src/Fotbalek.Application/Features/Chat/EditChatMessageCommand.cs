using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Chat;

/// <summary>
/// Author-only edit: replaces the body in place (same trim/clamp as send) and stamps EditedAt.
/// Tombstones can't be edited; an unchanged body is a silent no-op.
/// No banner and no unread impact — only the MessageEdited event fires.
/// </summary>
public sealed record EditChatMessageCommand(int TeamId, int MessageId, string NewBody) : ICommand;

internal sealed class EditChatMessageCommandHandler(
    IAppDbContext db,
    IUserContext userContext,
    TeamAccess teamAccess,
    IEventCollector events)
    : ICommandHandler<EditChatMessageCommand>
{
    private static readonly Error EditFailed =
        Error.Conflict("Chat.EditFailed", "The message could not be edited.");

    public async Task<Result> Handle(EditChatMessageCommand command, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not int userId)
            return Result.Failure(CommonErrors.NotAuthenticated);

        var newBody = (command.NewBody ?? string.Empty).Trim();
        if (newBody.Length == 0)
            return Result.Failure(Error.Validation("Chat.Empty", "The message is empty."));
        if (newBody.Length > Constants.Chat.MaxMessageLength)
            newBody = newBody[..Constants.Chat.MaxMessageLength];

        if (!await teamAccess.IsMemberAsync(command.TeamId, cancellationToken))
            return Result.Failure(CommonErrors.NotMember);

        var message = await db.ChatMessages
            .FirstOrDefaultAsync(m => m.Id == command.MessageId && m.TeamId == command.TeamId, cancellationToken);
        if (message == null || message.SenderUserId != userId || message.IsDeleted)
            return Result.Failure(EditFailed);
        if (message.Body == newBody)
            return Result.Success();

        message.Body = newBody;
        message.EditedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        events.Enqueue(new ChatMessageEditedEvent(command.TeamId, command.MessageId, newBody, message.EditedAt.Value));
        return Result.Success();
    }
}
