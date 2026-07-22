using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.SharedKernel;

namespace Fotbalek.Application.Features.Chat;

/// <summary>Advances the user's watermark for a team (monotonic — never rewinds, so multiple
/// tabs and out-of-order events are safe) and raises ReadStateChanged.</summary>
public sealed record MarkChatReadCommand(int TeamId, int MessageId) : ICommand;

internal sealed class MarkChatReadCommandHandler(
    IAppDbContext db,
    IUserContext userContext,
    TeamAccess teamAccess,
    IEventCollector events)
    : ICommandHandler<MarkChatReadCommand>
{
    public async Task<Result> Handle(MarkChatReadCommand command, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not int userId)
            return Result.Failure(CommonErrors.NotAuthenticated);
        if (!await teamAccess.IsMemberAsync(command.TeamId, cancellationToken))
            return Result.Success(); // silently ignore, as today

        await ChatReadStateAdvancer.AdvanceAsync(db, events, userId, command.TeamId, command.MessageId, cancellationToken);
        return Result.Success();
    }
}
