using FluentValidation;
using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Contracts.Players;
using Fotbalek.Domain.Entities;
using Fotbalek.SharedKernel;

namespace Fotbalek.Application.Features.Players;

/// <summary>
/// Creates the CALLER's player in a team (the claim screen's "create new" path). Requires
/// membership and no already-claimed player. With <paramref name="AutoSuffixName"/> a taken
/// name gets a numeric suffix instead of failing (the auto-create path).
/// </summary>
public sealed record CreateMyPlayerCommand(int TeamId, string Name, int AvatarId, bool AutoSuffixName = false)
    : ICommand<PlayerDto>;

internal sealed class CreateMyPlayerCommandValidator : AbstractValidator<CreateMyPlayerCommand>
{
    public CreateMyPlayerCommandValidator()
    {
        RuleFor(c => c.Name)
            .Must(n => n?.Trim().Length is >= 1 and <= PlayerRules.NameMaxLength)
            .WithMessage($"Name must be 1-{PlayerRules.NameMaxLength} characters.");
    }
}

internal sealed class CreateMyPlayerCommandHandler(IAppDbContext db, IUserContext userContext, TeamAccess teamAccess)
    : ICommandHandler<CreateMyPlayerCommand, PlayerDto>
{
    public async Task<Result<PlayerDto>> Handle(CreateMyPlayerCommand command, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not int userId)
            return Result.Failure<PlayerDto>(CommonErrors.NotAuthenticated);
        if (!await teamAccess.IsMemberAsync(command.TeamId, cancellationToken))
            return Result.Failure<PlayerDto>(CommonErrors.NotMember);

        if (await PlayerRules.HasClaimedPlayerAsync(db, command.TeamId, userId, cancellationToken))
            return Result.Failure<PlayerDto>(Error.Conflict(
                "Players.AlreadyClaimed", "You already have a player in this team."));

        var name = command.Name.Trim();
        if (await PlayerRules.IsNameTakenAsync(db, command.TeamId, name, null, cancellationToken))
        {
            if (!command.AutoSuffixName)
                return Result.Failure<PlayerDto>(PlayerRules.NameTaken);

            var baseName = name;
            var suffix = 2;
            do
            {
                name = Truncate($"{baseName} {suffix}");
                suffix++;
            } while (await PlayerRules.IsNameTakenAsync(db, command.TeamId, name, null, cancellationToken));
        }

        var player = new Player
        {
            TeamId = command.TeamId,
            UserId = userId,
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

    private static string Truncate(string input) =>
        input.Length <= PlayerRules.NameMaxLength ? input : input[..PlayerRules.NameMaxLength];
}
