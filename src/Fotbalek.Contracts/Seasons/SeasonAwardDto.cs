namespace Fotbalek.Contracts.Seasons;

/// <summary>
/// A stored season award. Season fields are populated by the player-trophy-case query;
/// partner fields only for Category = "Pair".
/// </summary>
public record SeasonAwardDto(
    int Id,
    int SeasonId,
    string? SeasonName,
    DateTimeOffset? SeasonStartsAt,
    int PlayerId,
    string PlayerName,
    int PlayerAvatarId,
    string Category,
    int Rank,
    int? PartnerPlayerId,
    string? PartnerPlayerName,
    int? PartnerPlayerAvatarId);

/// <summary>Champion of a closed season — the Player-gold award holder or the standings leader.</summary>
public record SeasonChampionDto(int PlayerId, string Name, int AvatarId, bool IsAwardHolder);
