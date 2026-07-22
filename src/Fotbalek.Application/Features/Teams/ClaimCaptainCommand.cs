using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Teams;

/// <summary>
/// Atomically claim the captain role for a team if it currently has no captain
/// and the caller is a member.
/// </summary>
public sealed record ClaimCaptainCommand(int TeamId) : ICommand;

internal sealed class ClaimCaptainCommandHandler(IAppDbContext db, IUserContext userContext, TeamAccess teamAccess)
    : ICommandHandler<ClaimCaptainCommand>
{
    public async Task<Result> Handle(ClaimCaptainCommand command, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not int userId)
            return Result.Failure(CommonErrors.NotAuthenticated);
        if (!await teamAccess.IsMemberAsync(command.TeamId, cancellationToken))
            return Result.Failure(CommonErrors.NotMember);

        var rows = await db.Teams
            .Where(t => t.Id == command.TeamId && t.CaptainUserId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.CaptainUserId, userId), cancellationToken);
        return rows > 0
            ? Result.Success()
            : Result.Failure(Error.Conflict("Teams.CaptainTaken", "The team already has a captain."));
    }
}
