using Fotbalek.Contracts.Matches;
using Fotbalek.Domain.Entities;

namespace Fotbalek.Application.Features.Matches;

public static class MatchMappings
{
    /// <summary>Requires MatchPlayers (with Player) loaded; Season is optional.</summary>
    public static MatchDto ToDto(this Match match) =>
        new(match.Id,
            match.TeamId,
            match.SeasonId,
            match.Season?.Name,
            match.Team1Score,
            match.Team2Score,
            match.PlayedAt,
            match.CreatedAt,
            match.MatchPlayers
                .OrderBy(mp => mp.TeamNumber)
                .ThenBy(mp => mp.Position)
                .Select(mp => mp.ToDto())
                .ToList());

    public static MatchPlayerDto ToDto(this MatchPlayer mp) =>
        new(mp.Id,
            mp.PlayerId,
            mp.Player.Name,
            mp.Player.AvatarId,
            mp.TeamNumber,
            mp.Position,
            mp.EloChange,
            mp.EloBefore,
            mp.EloAfter,
            mp.SeasonEloBefore,
            mp.SeasonEloAfter,
            mp.SeasonEloChange);
}
