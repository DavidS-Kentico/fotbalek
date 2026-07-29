namespace Fotbalek.Contracts.Matches;

/// <summary>
/// Whether the rules allow deleting a match (time window, closed season, later matches). Carries the
/// blocker, not a sentence — <see cref="CanDelete"/> is derived from it so the two cannot disagree.
/// </summary>
public record MatchDeletabilityDto(MatchDeletionBlocker Blocker)
{
    /// <summary>True when nothing blocks the deletion.</summary>
    public bool CanDelete => Blocker == MatchDeletionBlocker.None;
}

/// <summary>Ids of the matches immediately newer/older than a given match within its team.</summary>
public record AdjacentMatchIdsDto(int? NewerId, int? OlderId);
