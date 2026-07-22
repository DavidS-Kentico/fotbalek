using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Contracts.Teams;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Teams;

/// <summary>
/// Joins the caller to a team via an unexpired share link. <paramref name="ExpectedCodeName"/>
/// guards against a token pasted onto a different team's join URL.
/// </summary>
public sealed record JoinTeamByShareTokenCommand(string Token, string ExpectedCodeName) : ICommand<TeamDto>;

internal sealed class JoinTeamByShareTokenCommandHandler(
    IAppDbContext db,
    IUserContext userContext)
    : ICommandHandler<JoinTeamByShareTokenCommand, TeamDto>
{
    public async Task<Result<TeamDto>> Handle(JoinTeamByShareTokenCommand command, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not int userId)
            return Result.Failure<TeamDto>(CommonErrors.NotAuthenticated);

        var team = await db.ShareTokens.AsNoTracking()
            .Where(st => st.Token == command.Token && st.ExpiresAt > DateTimeOffset.UtcNow)
            .Select(st => st.Team)
            .FirstOrDefaultAsync(cancellationToken);

        if (team is null || !team.CodeName.Equals(command.ExpectedCodeName, StringComparison.OrdinalIgnoreCase))
            return Result.Failure<TeamDto>(Error.Unauthorized(
                "Teams.InvalidShareToken", "The share link has expired or is invalid."));

        await JoinMembership.EnsureMemberAsync(db, userId, team.Id, cancellationToken);
        return team.ToDto();
    }
}
