namespace Fotbalek.Contracts.Landing;

/// <summary>
/// Public landing-page statistics. Everything here is deliberately privacy-safe:
/// only cross-team aggregates and anonymized scores are exposed — never a team's
/// name/code or a player's identity.
/// </summary>
public record LandingStatsDto(
    LandingTotalsDto Totals,
    List<LandingRecentScoreDto> RecentScores,
    List<LandingActivityPointDto> Activity,
    LandingFunFactsDto FunFacts);

public record LandingTotalsDto(
    int Teams,
    int Players,
    int Matches,
    int MatchesThisWeek,
    int TotalGoals);

/// <summary>A recent match reduced to its score and time — no team identity.</summary>
public record LandingRecentScoreDto(int Team1Score, int Team2Score, DateTimeOffset PlayedAt);

public record LandingActivityPointDto(DateTime Day, int Matches);

public record LandingFunFactsDto(
    int MatchesToday,
    double AverageGoalsPerMatch,
    string? BiggestBlowout);
