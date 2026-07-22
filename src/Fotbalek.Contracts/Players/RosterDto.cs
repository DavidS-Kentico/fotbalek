using Fotbalek.Contracts.Seasons;
using Fotbalek.Contracts.Stats;

namespace Fotbalek.Contracts.Players;

/// <summary>
/// Everything the roster page renders in one payload, on the default lens: with an active season,
/// ratings/ranks/summaries/badges come from the season; otherwise all-time.
/// SeasonEloByPlayer only contains players with a ladder row — a roster player with no seasonal
/// match has no entry and renders as unrated.
/// </summary>
public record RosterDto(
    List<PlayerDto> Players,
    SeasonDto? ActiveSeason,
    Dictionary<int, int> SeasonEloByPlayer,
    Dictionary<int, PlayerSummaryDto> Summaries,
    Dictionary<int, int> Ranks,
    List<StatResult> Badges);
