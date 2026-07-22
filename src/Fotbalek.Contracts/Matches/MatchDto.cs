namespace Fotbalek.Contracts.Matches;

public record MatchDto(
    int Id,
    int TeamId,
    int? SeasonId,
    string? SeasonName,
    int Team1Score,
    int Team2Score,
    DateTimeOffset PlayedAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<MatchPlayerDto> MatchPlayers);

public record MatchPlayerDto(
    int Id,
    int PlayerId,
    string PlayerName,
    int PlayerAvatarId,
    int TeamNumber,
    string Position,
    int EloChange,
    int EloBefore,
    int EloAfter,
    int? SeasonEloBefore,
    int? SeasonEloAfter,
    int? SeasonEloChange);
