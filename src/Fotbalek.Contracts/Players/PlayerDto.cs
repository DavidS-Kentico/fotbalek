namespace Fotbalek.Contracts.Players;

public record PlayerDto(
    int Id,
    int TeamId,
    int? UserId,
    string Name,
    int AvatarId,
    int Elo,
    bool IsActive,
    DateTimeOffset CreatedAt);
