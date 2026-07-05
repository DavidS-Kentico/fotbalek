namespace Fotbalek.Web.Data.Entities;

/// <summary>
/// Frozen per-player season results — inserted once inside the close transaction and never updated.
/// Result rows exist if and only if the season is closed; the PK doubles as an idempotency backstop
/// for the lazy close (a duplicate close attempt fails structurally on insert).
/// </summary>
public class SeasonPlayerResult
{
    /// <summary>PK = FK — 1:1 with the ladder row.</summary>
    public int SeasonPlayerId { get; set; }

    /// <summary>Null = player was inactive at close, excluded from frozen standings.</summary>
    public int? FinalRank { get; set; }

    /// <summary>Wins by score.</summary>
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int MatchesPlayed { get; set; }

    /// <summary>Longest win streak within the season (score-based, chronological order).</summary>
    public int LongestWinStreak { get; set; }
    public int LongestLossStreak { get; set; }

    public int GoalkeeperMatches { get; set; }
    public int GoalsConcededAsGoalkeeper { get; set; }
    public int AttackerMatches { get; set; }
    public int GoalsScoredAsAttacker { get; set; }

    public SeasonPlayer SeasonPlayer { get; set; } = null!;
}
