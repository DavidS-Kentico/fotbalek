namespace Fotbalek.Contracts.Matches;

/// <summary>Whether the rules allow deleting a match (time window, closed season, later matches).</summary>
public record MatchDeletabilityDto(bool CanDelete, string? Reason);

/// <summary>Ids of the matches immediately newer/older than a given match within its team.</summary>
public record AdjacentMatchIdsDto(int? NewerId, int? OlderId);
