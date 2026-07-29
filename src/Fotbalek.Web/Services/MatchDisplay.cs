using Fotbalek.Contracts.Matches;
using Fotbalek.SharedKernel;

namespace Fotbalek.Web.Services;

/// <summary>
/// Display-string side of the match rules (English UI text — stays in Web; the rules themselves live
/// in Application's MatchRules, which returns a <see cref="MatchDeletionBlocker"/> and no wording).
/// </summary>
public static class MatchDisplay
{
    /// <summary>
    /// Why the delete button is locked, or null when nothing blocks it. Exhaustive with no fallback
    /// arm on purpose: a blocker added without its copy is a build error (CS8509), not a lock the UI
    /// cannot explain.
    /// </summary>
#pragma warning disable CS8524 // Only the named members exist; an out-of-range cast is a bug worth throwing on.
    public static string? BlockedReason(this MatchDeletionBlocker blocker) => blocker switch
    {
        MatchDeletionBlocker.None => null,
        MatchDeletionBlocker.NotFound => "Match not found",
        MatchDeletionBlocker.DeletionWindowElapsed =>
            $"Matches can only be deleted within {Constants.TimeThresholds.MatchDeletionWindowHours} hours of creation",
        MatchDeletionBlocker.SeasonClosed => "This match belongs to a closed season — its results are frozen",
        MatchDeletionBlocker.LaterMatchPlayed => "one or more players have played matches after this one",
    };
#pragma warning restore CS8524
}
