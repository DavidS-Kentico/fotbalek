using Fotbalek.SharedKernel;

namespace Fotbalek.Contracts.Stats;

/// <summary>
/// Per-player aggregates on the selected ladder (all-time or seasonal). Wins are determined
/// by score in both scopes.
/// </summary>
public class PlayerStats
{
    public int CurrentElo { get; set; } = Constants.Elo.DefaultRating;
    public int HighestElo { get; set; } = Constants.Elo.DefaultRating;
    public int LowestElo { get; set; } = Constants.Elo.DefaultRating;
    public int TotalMatches { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public double WinRate { get; set; }
    public int CurrentStreak { get; set; }
    public int LongestWinStreak { get; set; }
    public int LongestLossStreak { get; set; }
    public double AvgEloChangeOnWin { get; set; }
    public double AvgEloChangeOnLoss { get; set; }
    public int AvgOpponentElo { get; set; }
    public int AvgTeammateElo { get; set; }
    public List<bool> RecentForm { get; set; } = [];
    public double RecentFormWinRate { get; set; }
    public int CarriedCount { get; set; }
    public int CarryCount { get; set; }
    public double TeammateVariety { get; set; }
    public int UniqueTeammates { get; set; }
    public int ActiveRosterPartners { get; set; }
    public bool HasEnoughGamesForVariety { get; set; }
    public string PreferredPosition { get; set; } = "Flexible";
    public string BetterPosition { get; set; } = "-";
    public int GamesAsGk { get; set; }
    public int GamesAsAtk { get; set; }
    public int WinsAsGk { get; set; }
    public int WinsAsAtk { get; set; }
    public double WinRateAsGk { get; set; }
    public double WinRateAsAtk { get; set; }
    public int GoalsScoredAsGk { get; set; }
    public int GoalsConcededAsGk { get; set; }
    public int GoalsScoredAsAtk { get; set; }
    public int GoalsConcededAsAtk { get; set; }
    public string? BestPartner { get; set; }
    public double BestPartnerWinRate { get; set; }
    public int BestPartnerGames { get; set; }
    public string? WorstPartner { get; set; }
    public double WorstPartnerWinRate { get; set; }
    public int WorstPartnerGames { get; set; }
    public string? EasiestEnemy { get; set; }
    public double EasiestEnemyWinRate { get; set; }
    public int EasiestEnemyGames { get; set; }
    public string? HardestEnemy { get; set; }
    public double HardestEnemyWinRate { get; set; }
    public int HardestEnemyGames { get; set; }
    public int UnderTableCount { get; set; }
    public int TableSenderCount { get; set; }
    public List<EloHistoryPoint> EloHistory { get; set; } = [];

    // ELO expectation vs. reality
    public double ExpectedWins { get; set; }
    public double WinsVsExpected => Wins - ExpectedWins;

    // Goal margins
    public double AvgWinMargin { get; set; }
    public double AvgLossMargin { get; set; }

    // Performance bucketed by opponent strength (avg opp ELO vs own pre-match ELO)
    public int GamesVsStronger { get; set; }
    public int WinsVsStronger { get; set; }
    public double WinRateVsStronger { get; set; }
    public int GamesVsWeaker { get; set; }
    public int WinsVsWeaker { get; set; }
    public double WinRateVsWeaker { get; set; }

    // Clean sheets (matches as GK with 0 conceded)
    public int CleanSheetsAsGk { get; set; }

    // Full relationship lists
    public List<RelationshipStat> Partners { get; set; } = [];
    public List<RelationshipStat> Enemies { get; set; } = [];

    // Milestones
    public DateTimeOffset? FirstMatchDate { get; set; }
    public DateTimeOffset? PeakEloDate { get; set; }
    public int BiggestEloGain { get; set; }
    public DateTimeOffset? BiggestEloGainDate { get; set; }
    public int BiggestEloLoss { get; set; }
    public DateTimeOffset? BiggestEloLossDate { get; set; }

    // Activity
    public List<ActivityMonth> MatchesByMonth { get; set; } = [];
    public List<DayOfWeekStat> MatchesByDayOfWeek { get; set; } = [];
}

public sealed record ActivityMonth(int Year, int Month, int Games);

public sealed record DayOfWeekStat(DayOfWeek Day, int Games, int Wins)
{
    public double WinRate => Games > 0 ? (double)Wins / Games * 100 : 0;
    public int Losses => Games - Wins;
}

public class RelationshipStat
{
    public int PlayerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int AvatarId { get; set; }
    public int Games { get; set; }
    public int Wins { get; set; }
    public double WinRate { get; set; }
    public double AvgEloChange { get; set; }
    public int Losses => Games - Wins;
}

public class EloHistoryPoint
{
    public DateTimeOffset Date { get; set; }
    public int Elo { get; set; }
}
