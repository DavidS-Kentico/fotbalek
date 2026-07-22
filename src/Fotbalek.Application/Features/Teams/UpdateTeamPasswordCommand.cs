using FluentValidation;
using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Teams;

/// <summary>Updates the team password. Caller must be the team captain.</summary>
public sealed record UpdateTeamPasswordCommand(int TeamId, string Password) : ICommand;

internal sealed class UpdateTeamPasswordCommandValidator : AbstractValidator<UpdateTeamPasswordCommand>
{
    public UpdateTeamPasswordCommandValidator()
    {
        RuleFor(c => c.Password)
            .MinimumLength(4).WithMessage("Password must be at least 4 characters.")
            .MaximumLength(100).WithMessage("Password must be at most 100 characters.");
    }
}

internal sealed class UpdateTeamPasswordCommandHandler(
    IAppDbContext db,
    TeamAccess teamAccess,
    ITeamPasswordHasher passwordHasher)
    : ICommandHandler<UpdateTeamPasswordCommand>
{
    public async Task<Result> Handle(UpdateTeamPasswordCommand command, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsCaptainAsync(command.TeamId, cancellationToken))
            return Result.Failure(CommonErrors.NotCaptain);

        var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == command.TeamId, cancellationToken);
        if (team is null)
            return Result.Failure(Error.NotFound("Teams.NotFound", "Team not found."));

        team.PasswordHash = passwordHasher.Hash(command.Password);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
