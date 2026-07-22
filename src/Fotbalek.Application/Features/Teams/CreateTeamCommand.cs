using FluentValidation;
using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Contracts.Teams;
using Fotbalek.Domain.Entities;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Teams;

/// <summary>Creates a team with the caller as captain and joins them as the first member.</summary>
public sealed record CreateTeamCommand(string Name, string CodeName, string Password) : ICommand<TeamDto>;

internal sealed class CreateTeamCommandValidator : AbstractValidator<CreateTeamCommand>
{
    public CreateTeamCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().WithMessage("Team name is required.")
            .MaximumLength(100).WithMessage("Team name must be at most 100 characters.");
        RuleFor(c => c.CodeName).NotEmpty().WithMessage("Team URL code is required.")
            .MinimumLength(3).WithMessage("Team URL code must be at least 3 characters.")
            .MaximumLength(50).WithMessage("Team URL code must be at most 50 characters.")
            .Matches("^[a-z0-9-]+$").WithMessage("Only lowercase letters, numbers, and hyphens allowed.");
        RuleFor(c => c.Password)
            .MinimumLength(4).WithMessage("Password must be at least 4 characters.")
            .MaximumLength(100).WithMessage("Password must be at most 100 characters.");
    }
}

internal sealed class CreateTeamCommandHandler(
    IAppDbContext db,
    IUserContext userContext,
    ITeamPasswordHasher passwordHasher)
    : ICommandHandler<CreateTeamCommand, TeamDto>
{
    public async Task<Result<TeamDto>> Handle(CreateTeamCommand command, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not int userId)
            return Result.Failure<TeamDto>(CommonErrors.NotAuthenticated);

        var codeName = command.CodeName.ToLowerInvariant();
        if (await db.Teams.AnyAsync(t => t.CodeName == codeName, cancellationToken))
            return Result.Failure<TeamDto>(Error.Conflict(
                "Teams.CodeNameTaken", "This URL code is already taken. Please choose a different one."));

        var team = new Team
        {
            Name = command.Name.Trim(),
            CodeName = codeName,
            PasswordHash = passwordHasher.Hash(command.Password),
            CaptainUserId = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Teams.Add(team);

        db.TeamMemberships.Add(new TeamMembership
        {
            Team = team,
            UserId = userId,
            JoinedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
        return team.ToDto();
    }
}
