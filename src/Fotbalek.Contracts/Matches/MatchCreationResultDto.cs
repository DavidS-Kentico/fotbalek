using Fotbalek.Contracts.Seasons;

namespace Fotbalek.Contracts.Matches;

/// <summary>
/// Result of match creation. <paramref name="SeasonalFallback"/> is true when a seasonal match
/// was requested but the season ended (or was closed) between form load and submit — the match
/// was recorded off-season and the user should be notified.
/// </summary>
public record MatchCreationResultDto(MatchDto Match, SeasonDto? Season, bool SeasonalFallback);
