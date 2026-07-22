using FluentValidation;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Contracts.Players;
using Fotbalek.Domain.Entities;
using Fotbalek.SharedKernel;

namespace Fotbalek.Application.Features.Players;

/// <summary>Creates a placeholder player (no associated user). Captain only.</summary>
public sealed record CreatePlaceholderPlayerCommand(int TeamId, string Name, int AvatarId) : ICommand<PlayerDto>;

internal sealed class CreatePlaceholderPlayerCommandValidator : AbstractValidator<CreatePlaceholderPlayerCommand>
{
    public CreatePlaceholderPlayerCommandValidator()
    {
        RuleFor(c => c.Name)
            .Must(n => n?.Trim().Length is >= 1 and <= PlayerRules.NameMaxLength)
            .WithMessage($"Name must be 1-{PlayerRules.NameMaxLength} characters.");
    }
}

internal sealed class CreatePlaceholderPlayerCommandHandler(IAppDbContext db, TeamAccess teamAccess)
    : ICommandHandler<CreatePlaceholderPlayerCommand, PlayerDto>
{
    public async Task<Result<PlayerDto>> Handle(CreatePlaceholderPlayerCommand command, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsCaptainAsync(command.TeamId, cancellationToken))
            return Result.Failure<PlayerDto>(Error.Forbidden(
                "Players.NotCaptain", "Only the team captain can add players."));

        var name = command.Name.Trim();
        if (await PlayerRules.IsNameTakenAsync(db, command.TeamId, name, null, cancellationToken))
            return Result.Failure<PlayerDto>(PlayerRules.NameTaken);

        var player = new Player
        {
            TeamId = command.TeamId,
            UserId = null,
            Name = name,
            AvatarId = command.AvatarId,
            Elo = Constants.Elo.DefaultRating,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Players.Add(player);
        await db.SaveChangesAsync(cancellationToken);
        return player.ToDto();
    }
}
