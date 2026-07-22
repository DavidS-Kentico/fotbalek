using Fotbalek.Contracts.Players;
using Fotbalek.Domain.Entities;

namespace Fotbalek.Application.Features.Players;

public static class PlayerMappings
{
    public static PlayerDto ToDto(this Player player) =>
        new(player.Id, player.TeamId, player.UserId, player.Name, player.AvatarId,
            player.Elo, player.IsActive, player.CreatedAt);
}
