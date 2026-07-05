using Fotbalek.Web.Data.Entities;

namespace Fotbalek.Web.Services.Stats.Core;

/// <summary>Which ELO ladder the context's accessors read.</summary>
public enum EloLadder
{
    AllTime,
    Season
}

/// <summary>
/// Input shared by every stat calculation. Matches are pre-filtered to the requested period and pre-ordered by date asc.
/// Carries two orthogonal flags: <see cref="Ladder"/> — which fields the ELO accessors read — and
/// <see cref="IsFullScope"/> — true when the whole selected scope is shown, false when a time period
/// filter narrows it further.
/// </summary>
public class StatContext
{
    public required IReadOnlyList<Match> Matches { get; init; }
    public required IReadOnlyDictionary<int, Player> PlayersById { get; init; }
    public required EloLadder Ladder { get; init; }
    public required bool IsFullScope { get; init; }

    /// <summary>
    /// The season's participant map (PlayerId → ladder row), set in season scope. A roster player
    /// with no seasonal match has no entry and never defaults into the ladder at 1000.
    /// </summary>
    public IReadOnlyDictionary<int, SeasonPlayer>? SeasonPlayersById { get; init; }

    public bool IsAllTime => Ladder == EloLadder.AllTime && IsFullScope;

    /// <summary>
    /// The player pool for pool-based stats. In season scope: active players that actually
    /// participated in the season (have a SeasonPlayer row).
    /// </summary>
    public IEnumerable<Player> ActivePlayers => Ladder == EloLadder.Season
        ? PlayersById.Values.Where(p => p.IsActive && SeasonPlayersById?.ContainsKey(p.Id) == true)
        : PlayersById.Values.Where(p => p.IsActive);

    // Ladder-aware ELO accessors — stats read these instead of the MatchPlayer/Player properties,
    // so season badges show seasonal numbers, not all-time ones.

    public int EloBeforeOf(MatchPlayer mp) =>
        Ladder == EloLadder.Season ? mp.SeasonEloBefore ?? Constants.Elo.DefaultRating : mp.EloBefore;

    public int EloAfterOf(MatchPlayer mp) =>
        Ladder == EloLadder.Season ? mp.SeasonEloAfter ?? Constants.Elo.DefaultRating : mp.EloAfter;

    public int EloChangeOf(MatchPlayer mp) =>
        Ladder == EloLadder.Season ? mp.SeasonEloChange ?? 0 : mp.EloChange;

    /// <summary>Current ELO of the selected ladder: SeasonPlayer.Elo in season scope (default 1000 with no row), Player.Elo otherwise.</summary>
    public int CurrentEloOf(Player player) => Ladder == EloLadder.Season
        ? (SeasonPlayersById != null && SeasonPlayersById.TryGetValue(player.Id, out var sp)
            ? sp.Elo
            : Constants.Elo.DefaultRating)
        : player.Elo;
}
