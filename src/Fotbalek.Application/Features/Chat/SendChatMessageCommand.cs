using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Contracts.Chat;
using Fotbalek.Domain.Entities;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Chat;

/// <summary>
/// Persists a message and enqueues the post-commit notifications. Sending inherently advances
/// the sender's read watermark — the send context is open + selected + focused. The in-memory
/// send throttle runs in Web BEFORE dispatch (rate limiting is a host concern, §4.4).
/// </summary>
public sealed record SendChatMessageCommand(int TeamId, string Body) : ICommand<ChatMessageDto>;

internal sealed class SendChatMessageCommandHandler(
    IAppDbContext db,
    IUserContext userContext,
    TeamAccess teamAccess,
    IEventCollector events,
    INotificationWriter notifications)
    : ICommandHandler<SendChatMessageCommand, ChatMessageDto>
{
    public async Task<Result<ChatMessageDto>> Handle(SendChatMessageCommand command, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not int userId)
            return Result.Failure<ChatMessageDto>(CommonErrors.NotAuthenticated);

        if (!ChatMessageContent.TryNormalize(command.Body, out var body))
            return Result.Failure<ChatMessageDto>(Error.Validation("Chat.Empty", "The message is empty."));

        if (!await teamAccess.IsMemberAsync(command.TeamId, cancellationToken))
            return Result.Failure<ChatMessageDto>(CommonErrors.NotMember);

        var message = new ChatMessage
        {
            TeamId = command.TeamId,
            SenderUserId = userId,
            Body = body,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ChatMessages.Add(message);
        await db.SaveChangesAsync(cancellationToken);

        await ChatReadStateAdvancer.AdvanceAsync(db, events, userId, command.TeamId, message.Id, cancellationToken);

        // A mention leaves a permanent bell row alongside chat's transient banner — the one point the
        // two features overlap, deliberately (AI/notifications.md §5.2). One extra roster query per
        // send, which nothing else here can share.
        await ChatMentionWriter.WriteAsync(db, notifications, command.TeamId, userId, message.Id, body, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var sender = await ResolveSenderAsync(command.TeamId, userId, cancellationToken);
        var dto = new ChatMessageDto(
            message.Id, userId, sender.Name, sender.AvatarId,
            body, message.CreatedAt, IsDeleted: false, EditedAt: null, Reactions: []);
        events.Enqueue(new ChatMessagePostedEvent(command.TeamId, dto));
        return dto;
    }

    private async Task<(string Name, int AvatarId)> ResolveSenderAsync(int teamId, int userId, CancellationToken cancellationToken)
    {
        var player = await db.Players.AsNoTracking()
            .Where(p => p.TeamId == teamId && p.UserId == userId)
            .Select(p => new { p.Name, p.AvatarId })
            .FirstOrDefaultAsync(cancellationToken);
        if (player != null)
            return (player.Name, player.AvatarId);

        // Purely defensive — every dock participant has a claimed Player.
        var userName = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.UserName)
            .FirstOrDefaultAsync(cancellationToken);
        return (userName ?? "Unknown", 1);
    }
}
