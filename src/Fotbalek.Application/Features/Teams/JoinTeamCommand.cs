using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Contracts.Teams;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Teams;

/// <summary>Joins the caller to a team by code name + team password. Idempotent for existing members.</summary>
public sealed record JoinTeamCommand(string CodeName, string Password) : ICommand<TeamDto>;

internal sealed class JoinTeamCommandHandler(
    IAppDbContext db,
    IUserContext userContext,
    ITeamPasswordHasher passwordHasher)
    : ICommandHandler<JoinTeamCommand, TeamDto>
{
    public async Task<Result<TeamDto>> Handle(JoinTeamCommand command, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not int userId)
            return Result.Failure<TeamDto>(CommonErrors.NotAuthenticated);

        var codeName = command.CodeName.ToLowerInvariant();
        var team = await db.Teams.AsNoTracking()
            .FirstOrDefaultAsync(t => t.CodeName == codeName, cancellationToken);
        if (team is null)
            return Result.Failure<TeamDto>(Error.NotFound(
                "Teams.NotFound", "Team not found. Please check the team code."));

        if (!passwordHasher.Verify(command.Password, team.PasswordHash))
            return Result.Failure<TeamDto>(Error.Unauthorized(
                "Teams.InvalidPassword", "Invalid password."));

        await JoinMembership.EnsureMemberAsync(db, userId, team.Id, cancellationToken);
        return team.ToDto();
    }
}
