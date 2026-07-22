namespace Fotbalek.Contracts.Stats;

public class PlayerRanking
{
    public int Rank { get; set; }
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public int AvatarId { get; set; }
    public int Elo { get; set; }
    public int Matches { get; set; }
    public int Wins { get; set; }
    public double WinRate { get; set; }
}

/// <summary>One row of season standings — live (active season) or frozen (closed season).</summary>
public class SeasonStandingRow
{
    public int Rank { get; set; }
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public int AvatarId { get; set; }
    /// <summary>Seasonal ELO (final for closed seasons).</summary>
    public int Elo { get; set; }
    public int Matches { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public double WinRate { get; set; }
    public int LongestWinStreak { get; set; }
    public int LongestLossStreak { get; set; }
}

public class PositionRanking
{
    public int Rank { get; set; }
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public int AvatarId { get; set; }
    public int Games { get; set; }
    public int Goals { get; set; }
    public double AverageGoals { get; set; }
}

/// <summary>Both position tables together (single dispatch for the rankings page).</summary>
public record PositionRankingsDto(List<PositionRanking> Goalkeepers, List<PositionRanking> Attackers);

public class PairStats
{
    public int Player1Id { get; set; }
    public string Player1Name { get; set; } = string.Empty;
    public int Player1AvatarId { get; set; }
    public int Player2Id { get; set; }
    public string Player2Name { get; set; } = string.Empty;
    public int Player2AvatarId { get; set; }
    public int Matches { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public double WinRate { get; set; }
    public int TotalScore { get; set; }
    public double AverageScore { get; set; }
}
