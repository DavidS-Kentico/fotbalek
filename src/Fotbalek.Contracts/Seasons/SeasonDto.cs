namespace Fotbalek.Contracts.Seasons;

/// <summary>
/// A named, per-team time period that groups matches. Mirrors the Season entity's lifecycle
/// helpers so components keep their phase logic.
/// </summary>
public record SeasonDto(
    int Id,
    int TeamId,
    string Name,
    string? Description,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset CreatedAt)
{
    /// <summary>Closed: results are frozen and immutable.</summary>
    public bool IsClosed => ClosedAt != null;

    /// <summary>Active: currently accepting matches. At most one exists per team (non-overlap invariant).</summary>
    public bool IsActiveAt(DateTimeOffset now) =>
        ClosedAt == null && StartsAt <= now && (EndsAt == null || now < EndsAt);

    /// <summary>Ended, pending close: past its end date, waiting for the lazy close. Accepts no matches.</summary>
    public bool IsPendingCloseAt(DateTimeOffset now) =>
        ClosedAt == null && EndsAt != null && EndsAt <= now;

    /// <summary>Scheduled: created ahead of time; nothing is active about it until StartsAt arrives.</summary>
    public bool IsScheduledAt(DateTimeOffset now) =>
        ClosedAt == null && StartsAt > now;
}
