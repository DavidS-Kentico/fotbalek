using Fotbalek.Contracts.Teams;

namespace Fotbalek.Contracts.Players;

/// <summary>Player detail with its team and (when claimed) the owning user's name — the player-detail page header.</summary>
public sealed record PlayerWithTeamDto(PlayerDto Player, TeamDto Team, string? OwnerUserName);
