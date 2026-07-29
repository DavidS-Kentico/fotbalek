namespace Fotbalek.Contracts.Stats;

/// <summary>
/// Identifies a stat. Deliberately an enum rather than a string: the UI pairs every member with its
/// wording and styling in an exhaustive switch, so a stat added here without presentation fails the
/// build (CS8509) instead of quietly rendering wrong. Members are grouped by <see cref="StatTheme"/>
/// for readability only — nothing depends on their numeric values.
/// </summary>
public enum StatKey
{
    // Rankings
    TopRated,
    LastPlace,
    TopGainer,
    TopLoser,
    BestWinRate,

    // Streaks
    HotStreak,
    ColdStreak,
    StreakKing,
    SlumpKing,

    // Margins
    Destroyer,
    Lucker,
    TableDiver,
    TableSender,
    CardiacKid,

    // ELO swings
    BiggestEloWin,
    BiggestEloLoss,

    // Positions
    BestAttacker,
    BestGoalkeeper,

    // Rivalries
    Nemesis,

    // Partnerships
    BestFriend,
    WorstFriend,

    // Underdog
    GiantSlayer,
    ChokeArtist,

    // Career arc
    PeakElo,
    FurthestFromPeak,

    // Activity
    VarietyPlayer,

    // Special
    Carried,
    Newcomer,
    TomkoMemorial
}
