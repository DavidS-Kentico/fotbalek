namespace Fotbalek.Contracts.Seasons;

/// <summary>
/// The two lazy per-team season hooks in one query. Folded together deliberately: the lookup runs
/// on EVERY current-team resolution — including the cached fast path, so the check can't fire once
/// per multi-hour circuit — and a second query beside it would double a cost that is already paid
/// more often than it looks (AI/notifications.md §5.4).
/// <para>
/// The caller must run <see cref="DueClose"/> FIRST: a season can run its whole course between two
/// visits, and then only "ended" should be delivered — the announce command's
/// <c>ClosedAt == null</c> re-check is what suppresses the start announcement.
/// </para>
/// </summary>
public record TeamSeasonHooksDto(
    // Seasons past their end date and not yet closed.
    List<int> DueClose,
    // Started, still open, and never announced.
    List<int> Unannounced);
