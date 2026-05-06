using Fotbalek.Web.Data.Entities;

namespace Fotbalek.Web.Services.Stats.Core;

/// <summary>
/// Input shared by every stat calculation. Matches are pre-filtered to the requested period and pre-ordered by date asc.
/// </summary>
public class StatContext
{
    public required IReadOnlyList<Match> Matches { get; init; }
    public required IReadOnlyDictionary<int, Player> PlayersById { get; init; }
    public required bool IsAllTime { get; init; }

    public IEnumerable<Player> ActivePlayers => PlayersById.Values.Where(p => p.IsActive);
}
