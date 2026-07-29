namespace Fotbalek.Domain.Entities;

/// <summary>
/// The persisted "who is currently #1" snapshot that makes lead-change detection a comparison
/// instead of a before/after double computation (AI/notifications.md §3.4, §6).
/// <para>
/// <b>Row absence means "never evaluated", and that is load-bearing</b> — it is what makes the very
/// first evaluation of a (team, scope, category) silent, which is the equivalent of a backfill guard
/// for ladder leads (§3.6). Two accepted consequences: the first lead in a brand-new season is never
/// announced, and a closed season's rows are frozen by construction (no match can be added to a
/// closed season, so nothing re-evaluates it).
/// </para>
/// </summary>
public class LadderLeader
{
    public int Id { get; set; }

    public int TeamId { get; set; }

    /// <summary>Scope. <b>Null = the all-time ladder.</b> SQL Server's unique index allows a single
    /// NULL, so the unique (TeamId, SeasonId, Category) index permits exactly one all-time row per
    /// team and category — which is what we want.</summary>
    public int? SeasonId { get; set; }

    /// <summary>One of Constants.Seasons.AwardCategories: Player / Goalkeeper / Attacker / Pair.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>The current leader. For the Pair ladder, the lower player id.</summary>
    public int PlayerId { get; set; }

    /// <summary>Pair ladder only — the higher player id.</summary>
    public int? PartnerPlayerId { get; set; }

    public DateTimeOffset EvaluatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Team Team { get; set; } = null!;
    public Season? Season { get; set; }
    public Player Player { get; set; } = null!;
    public Player? PartnerPlayer { get; set; }
}
