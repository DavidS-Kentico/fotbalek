namespace Fotbalek.Web.Data.Entities;

/// <summary>
/// The live seasonal ladder row — one per player who participated in the season, created lazily on
/// their first seasonal match. Carries only the seasonal ELO; everything written at close lives in
/// <see cref="SeasonPlayerResult"/>, so live state and frozen results never share a row. A closed
/// season accepts no new matches and its matches cannot be deleted, so at close the Elo value simply
/// stops changing and is the final seasonal ELO.
/// </summary>
public class SeasonPlayer
{
    public int Id { get; set; }
    public int SeasonId { get; set; }
    public int PlayerId { get; set; }

    /// <summary>Seasonal ELO — updated only by this season's matches.</summary>
    public int Elo { get; set; } = Constants.Elo.DefaultRating;

    public Season Season { get; set; } = null!;
    public Player Player { get; set; } = null!;
    public SeasonPlayerResult? Result { get; set; }
}
