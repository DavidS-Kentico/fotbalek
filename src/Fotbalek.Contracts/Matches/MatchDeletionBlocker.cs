namespace Fotbalek.Contracts.Matches;

/// <summary>
/// Why the rules refuse to delete a match, or <see cref="None"/> when they allow it. An enum rather
/// than a sentence: the wording is the UI's business and lives there, paired with each member in an
/// exhaustive switch so a blocker added here without its copy fails the build (CS8509).
/// </summary>
public enum MatchDeletionBlocker
{
    /// <summary>Nothing blocks the deletion.</summary>
    None,

    /// <summary>The match no longer exists.</summary>
    NotFound,

    /// <summary>The match is older than the deletion window.</summary>
    DeletionWindowElapsed,

    /// <summary>The match belongs to a closed season, whose standings and awards are frozen.</summary>
    SeasonClosed,

    /// <summary>A participant has played since — reversing the ELO would corrupt the later matches.</summary>
    LaterMatchPlayed
}
