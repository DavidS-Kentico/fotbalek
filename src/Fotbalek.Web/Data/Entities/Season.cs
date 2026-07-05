namespace Fotbalek.Web.Data.Entities;

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
