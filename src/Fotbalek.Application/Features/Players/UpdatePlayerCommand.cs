using FluentValidation;
using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.SharedKernel;

namespace Fotbalek.Application.Features.Players;

/// <summary>Updates a player's name/avatar. Actor must be team captain OR own the player.</summary>
public sealed record UpdatePlayerCommand(int TeamId, int PlayerId, string Name, int AvatarId) : ICommand;

internal sealed class UpdatePlayerCommandValidator : AbstractValidator<UpdatePlayerCommand>
{
    public UpdatePlayerCommandValidator()
    {
        RuleFor(c => c.Name)
            .Must(n => n?.Trim().Length is >= 1 and <= PlayerRules.NameMaxLength)
            .WithMessage($"Name must be 1-{PlayerRules.NameMaxLength} characters.");
    }
}

internal sealed class UpdatePlayerCommandHandler(IAppDbContext db, IUserContext userContext, TeamAccess teamAccess)
    : ICommandHandler<UpdatePlayerCommand>
{
    public async Task<Result> Handle(UpdatePlayerCommand command, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not int userId)
            return Result.Failure(CommonErrors.NotAuthenticated);

        var player = await db.Players.FindAsync([command.PlayerId], cancellationToken);
        if (player is null || player.TeamId != command.TeamId)
            return Result.Failure(Error.NotFound("Players.NotFound", "Player not found."));

        var isCaptain = await teamAccess.IsCaptainAsync(command.TeamId, cancellationToken);
        var isOwner = player.UserId == userId;
        if (!isCaptain && !isOwner)
            return Result.Failure(Error.Forbidden(
                "Players.NotAllowed", "Only the captain or the player's owner can edit this player."));

        var name = command.Name.Trim();
        if (await PlayerRules.IsNameTakenAsync(db, command.TeamId, name, command.PlayerId, cancellationToken))
            return Result.Failure(PlayerRules.NameTaken);

        player.Name = name;
        player.AvatarId = command.AvatarId;
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
