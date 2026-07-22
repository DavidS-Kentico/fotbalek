using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Seasons;

/// <summary>Rename + edit description — allowed anytime, including closed seasons. Captain only.</summary>
public sealed record UpdateSeasonDetailsCommand(int SeasonId, string Name, string? Description) : ICommand;

internal sealed class UpdateSeasonDetailsCommandHandler(IAppDbContext db, TeamAccess teamAccess)
    : ICommandHandler<UpdateSeasonDetailsCommand>
{
    public async Task<Result> Handle(UpdateSeasonDetailsCommand command, CancellationToken cancellationToken)
    {
        var season = await db.Seasons.FirstOrDefaultAsync(s => s.Id == command.SeasonId, cancellationToken);
        if (season is null)
            return Result.Failure(Error.NotFound("Seasons.NotFound", "Season not found."));
        if (!await teamAccess.IsCaptainAsync(season.TeamId, cancellationToken))
            return Result.Failure(CommonErrors.NotCaptain);

        var name = command.Name;
        var description = command.Description;
        if (SeasonRules.ValidateNameAndDescription(ref name, ref description) is { } invalid)
            return Result.Failure(invalid);
        if (await SeasonRules.CheckNameAvailableAsync(db, season.TeamId, name, command.SeasonId, cancellationToken) is { } nameTaken)
            return Result.Failure(nameTaken);

        season.Name = name;
        season.Description = description;
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
