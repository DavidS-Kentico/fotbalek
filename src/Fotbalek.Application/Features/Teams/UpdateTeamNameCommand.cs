using FluentValidation;
using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Teams;

/// <summary>Updates the team display name. Caller must be the team captain.</summary>
public sealed record UpdateTeamNameCommand(int TeamId, string Name) : ICommand;

internal sealed class UpdateTeamNameCommandValidator : AbstractValidator<UpdateTeamNameCommand>
{
    public UpdateTeamNameCommandValidator()
    {
        RuleFor(c => c.Name)
            .Must(n => n?.Trim().Length is >= 1 and <= 100)
            .WithMessage("Name must be 1-100 characters.");
    }
}

internal sealed class UpdateTeamNameCommandHandler(IAppDbContext db, TeamAccess teamAccess)
    : ICommandHandler<UpdateTeamNameCommand>
{
    public async Task<Result> Handle(UpdateTeamNameCommand command, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsCaptainAsync(command.TeamId, cancellationToken))
            return Result.Failure(CommonErrors.NotCaptain);

        var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == command.TeamId, cancellationToken);
        if (team is null)
            return Result.Failure(Error.NotFound("Teams.NotFound", "Team not found."));

        team.Name = command.Name.Trim();
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
