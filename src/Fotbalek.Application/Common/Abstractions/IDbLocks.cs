namespace Fotbalek.Application.Common.Abstractions;

/// <summary>
/// Pessimistic SQL Server locks (raw SQL on the scope's connection — provider-specific, hence
/// implemented in Infrastructure). Both locks are Transaction-owned: they REQUIRE an open
/// transaction, which the TransactionBehavior guarantees for every command
/// (AI/architecture.md §3, §4.2).
/// </summary>
public interface IDbLocks
{
    /// <summary>
    /// Update lock on the Season row — every write touching a season's matches serializes
    /// through it (EF Core has no pessimistic-locking API). Held until the ambient
    /// transaction ends.
    /// </summary>
    Task LockSeasonRowAsync(int seasonId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Per-team application lock (sp_getapplock) serializing the writes that reshape the season
    /// timeline (create, EndsAt edit — creation has no Season row to lock yet). Held until the
    /// ambient transaction ends.
    /// </summary>
    Task AcquireTeamTimelineLockAsync(int teamId, CancellationToken cancellationToken = default);
}
