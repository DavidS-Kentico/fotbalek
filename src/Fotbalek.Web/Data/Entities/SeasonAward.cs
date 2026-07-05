namespace Fotbalek.Web.Data.Entities;

/// <summary>
/// Permanent, stored achievement generated once at season close — not a computed badge.
/// Pair awards create one row per member so lookups by PlayerId stay trivial.
/// </summary>
public class SeasonAward
{
    public int Id { get; set; }
    public int SeasonId { get; set; }
    public int PlayerId { get; set; }

    /// <summary>One of <see cref="Constants.Seasons.AwardCategories"/>: Player, Goalkeeper, Attacker, Pair.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>1-3 — gold / silver / bronze.</summary>
    public int Rank { get; set; }

    /// <summary>Only for Category = "Pair" — the teammate.</summary>
    public int? PartnerPlayerId { get; set; }

    public Season Season { get; set; } = null!;
    public Player Player { get; set; } = null!;
    public Player? PartnerPlayer { get; set; }
}
