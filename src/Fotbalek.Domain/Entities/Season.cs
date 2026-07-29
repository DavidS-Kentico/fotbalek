namespace Fotbalek.Domain.Entities;

/// <summary>
/// A named, per-team time period that groups matches. Each season has its own ELO ladder;
/// when it closes, final standings and awards are frozen into the Season* result tables.
/// </summary>
public class Season
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Inclusive start of the season period.</summary>
    public DateTimeOffset StartsAt { get; set; }

    /// <summary>Exclusive end of the season period. Null = open-ended.</summary>
    public DateTimeOffset? EndsAt { get; set; }

    /// <summary>When the season was closed and results frozen. Null = not yet closed.</summary>
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>
    /// When the "season started" notification was sent. Null until it has been. Mirrors
    /// <see cref="ClosedAt"/>: a single-row guard written under the season row lock, so concurrent
    /// page loads cannot double-announce without relying on a unique-index violation as flow
    /// control (AI/notifications.md §3.5).
    /// <para>
    /// Also stamped WITHOUT sending anything in the two cases where the announcement would be
    /// nonsense: a season created entirely in the past (it closes in the same round trip), and a
    /// season that ran its whole course before anyone opened a team page (§5.4).
    /// </para>
    /// </summary>
    public DateTimeOffset? StartAnnouncedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Team Team { get; set; } = null!;
    public ICollection<SeasonPlayer> SeasonPlayers { get; set; } = new List<SeasonPlayer>();
    public ICollection<SeasonPair> Pairs { get; set; } = new List<SeasonPair>();
    public ICollection<SeasonAward> Awards { get; set; } = new List<SeasonAward>();
    public ICollection<Match> Matches { get; set; } = new List<Match>();

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
