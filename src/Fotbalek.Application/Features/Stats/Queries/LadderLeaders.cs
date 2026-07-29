using Fotbalek.Domain.Entities;
using Fotbalek.Domain.Services;
using Fotbalek.SharedKernel;

namespace Fotbalek.Application.Features.Stats.Queries;

/// <summary>
/// The four ladders' ranking rules, in one place: solo, goalkeeper, attacker and pair — the same
/// four as <c>Constants.Seasons.AwardCategories</c> and the same four tables the Rankings page
/// renders. Every ordering chain ends in a PlayerId tie-break, so a tie always resolves to exactly
/// one row (AI/notifications.md §6.1).
/// <para>
/// It lives beside the queries that own the rules rather than in the notifications slice, because it
/// IS ranking logic — notifications merely consume it. All six ranking queries order through these
/// methods, and <see cref="Compute"/> picks the #1s the ladder-lead snapshot compares against, so the
/// bell and the page can never disagree.
/// </para>
/// <para>
/// <b>Why the final tie-break is not cosmetic:</b> the snapshot comparison asks "is the #1 the same
/// row as last time". Without a deterministic last resort, two players tied at the top can swap
/// places between two evaluations for no reason at all — and every swap looks exactly like a lead
/// change, firing a lost/taken pair on every single match, forever. Equal ELO is not exotic in a
/// small team: everyone starts at 1000 and <c>EloCalculator.ApplyEloChange</c> clamps at 100.
/// </para>
/// </summary>
internal static class LadderLeaders
{
    /// <summary>The four ladder categories, in the order the Rankings page renders them.</summary>
    public static readonly string[] Categories =
    [
        Constants.Seasons.AwardCategories.Player,
        Constants.Seasons.AwardCategories.Goalkeeper,
        Constants.Seasons.AwardCategories.Attacker,
        Constants.Seasons.AwardCategories.Pair,
    ];

    /// <summary>The #1 of one ladder. <see cref="PartnerPlayerId"/> is set for the pair ladder only,
    /// where <see cref="PlayerId"/> is the lower of the two ids.</summary>
    public sealed record LadderTop(int PlayerId, int? PartnerPlayerId);

    // ── Ordering keys ─────────────────────────────────────────────────────────────────────────

    /// <summary>Solo: ELO desc → wins desc → matches desc → PlayerId asc.</summary>
    public readonly record struct SoloKey(int Elo, int Wins, int Matches, int PlayerId);

    /// <summary>Position: goals per game (asc for GK, desc for ATK) → games desc → ELO desc → PlayerId asc.</summary>
    public readonly record struct PositionKey(double GoalsPerGame, int Games, int Elo, int PlayerId);

    /// <summary>Pair: win rate desc → matches desc → combined ELO desc → smaller PlayerId asc.</summary>
    public readonly record struct PairKey(double WinRate, int Matches, int CombinedElo, int MinPlayerId);

    // ── Ordering chains ───────────────────────────────────────────────────────────────────────

    public static IEnumerable<T> OrderSolo<T>(this IEnumerable<T> source, Func<T, SoloKey> key) =>
        source.Select(item => (Item: item, Key: key(item)))
            .OrderByDescending(x => x.Key.Elo)
            .ThenByDescending(x => x.Key.Wins)
            .ThenByDescending(x => x.Key.Matches)
            .ThenBy(x => x.Key.PlayerId)
            .Select(x => x.Item);

    public static IEnumerable<T> OrderGoalkeepers<T>(this IEnumerable<T> source, Func<T, PositionKey> key) =>
        source.Select(item => (Item: item, Key: key(item)))
            .OrderBy(x => x.Key.GoalsPerGame)
            .ThenByDescending(x => x.Key.Games)
            .ThenByDescending(x => x.Key.Elo)
            .ThenBy(x => x.Key.PlayerId)
            .Select(x => x.Item);

    public static IEnumerable<T> OrderAttackers<T>(this IEnumerable<T> source, Func<T, PositionKey> key) =>
        source.Select(item => (Item: item, Key: key(item)))
            .OrderByDescending(x => x.Key.GoalsPerGame)
            .ThenByDescending(x => x.Key.Games)
            .ThenByDescending(x => x.Key.Elo)
            .ThenBy(x => x.Key.PlayerId)
            .Select(x => x.Item);

    public static IEnumerable<T> OrderPairs<T>(this IEnumerable<T> source, Func<T, PairKey> key) =>
        source.Select(item => (Item: item, Key: key(item)))
            .OrderByDescending(x => x.Key.WinRate)
            .ThenByDescending(x => x.Key.Matches)
            .ThenByDescending(x => x.Key.CombinedElo)
            .ThenBy(x => x.Key.MinPlayerId)
            .Select(x => x.Item);

    // ── Eligibility ───────────────────────────────────────────────────────────────────────────

    /// <summary>A position table lists a player only past this many matches in that position.</summary>
    public static bool IsPositionEligible(int gamesInPosition) =>
        gamesInPosition >= Constants.TimeThresholds.MinGamesForPositionBadge;

    /// <summary>A duo is ranked only past this many matches together.</summary>
    public static bool IsPairEligible(int matchesTogether) =>
        matchesTogether >= Constants.TimeThresholds.MinGamesForPartnerStats;

    // ── Leaders ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The current #1 of each of the four ladders within one scope. A category is absent from the
    /// result when its ladder has nobody eligible — which the caller must treat as "delete the
    /// snapshot row", not as "unchanged".
    /// </summary>
    /// <param name="matches">The scope's matches, chronological, with their MatchPlayers loaded.</param>
    /// <param name="playersById">The team's players — the source of <c>IsActive</c>. All four ladders
    /// filter on it in both scopes.</param>
    /// <param name="eloOf">The scope's rating for a player, or null when the player is not on this
    /// ladder at all. All-time returns <c>Player.Elo</c> for everyone; a season returns the
    /// <c>SeasonPlayer.Elo</c> of participants and null for the rest, so a roster player with no
    /// seasonal match never defaults into the ladder at 1000.</param>
    public static Dictionary<string, LadderTop> Compute(
        IReadOnlyList<Match> matches,
        IReadOnlyDictionary<int, Player> playersById,
        Func<int, int?> eloOf)
    {
        var aggregates = SeasonAggregates.ComputeParticipants(matches);
        var result = new Dictionary<string, LadderTop>(StringComparer.Ordinal);

        bool OnLadder(int playerId) =>
            playersById.TryGetValue(playerId, out var player) && player.IsActive && eloOf(playerId) != null;

        // Solo — every eligible player counts, including one who has not played yet.
        var topSolo = playersById.Values
            .Where(p => OnLadder(p.Id))
            .OrderSolo(p =>
            {
                var agg = aggregates.GetValueOrDefault(p.Id);
                return new SoloKey(eloOf(p.Id)!.Value, agg?.Wins ?? 0, agg?.MatchesPlayed ?? 0, p.Id);
            })
            .FirstOrDefault();
        if (topSolo != null)
            result[Constants.Seasons.AwardCategories.Player] = new LadderTop(topSolo.Id, null);

        // Goalkeeper — fewest goals conceded per game played in goal.
        var topGoalkeeper = aggregates
            .Where(kv => OnLadder(kv.Key) && IsPositionEligible(kv.Value.GoalkeeperMatches))
            .Select(kv => new { PlayerId = kv.Key, Games = kv.Value.GoalkeeperMatches, Goals = kv.Value.GoalsConcededAsGoalkeeper })
            .OrderGoalkeepers(x => new PositionKey((double)x.Goals / x.Games, x.Games, eloOf(x.PlayerId)!.Value, x.PlayerId))
            .FirstOrDefault();
        if (topGoalkeeper != null)
            result[Constants.Seasons.AwardCategories.Goalkeeper] = new LadderTop(topGoalkeeper.PlayerId, null);

        // Attacker — most goals scored per game played up front.
        var topAttacker = aggregates
            .Where(kv => OnLadder(kv.Key) && IsPositionEligible(kv.Value.AttackerMatches))
            .Select(kv => new { PlayerId = kv.Key, Games = kv.Value.AttackerMatches, Goals = kv.Value.GoalsScoredAsAttacker })
            .OrderAttackers(x => new PositionKey((double)x.Goals / x.Games, x.Games, eloOf(x.PlayerId)!.Value, x.PlayerId))
            .FirstOrDefault();
        if (topAttacker != null)
            result[Constants.Seasons.AwardCategories.Attacker] = new LadderTop(topAttacker.PlayerId, null);

        // Pair — best win rate together. Inactive members are excluded in BOTH scopes, otherwise the
        // bell could announce that you lost the #1 duo spot to a pair that no longer exists (§6.1).
        var topPair = SeasonAggregates.ComputePairs(matches)
            .Where(kv => IsPairEligible(kv.Value.Matches) && OnLadder(kv.Key.Player1Id) && OnLadder(kv.Key.Player2Id))
            .Select(kv => new { kv.Key.Player1Id, kv.Key.Player2Id, kv.Value.Matches, kv.Value.Wins })
            .OrderPairs(x => new PairKey(
                (double)x.Wins / x.Matches,
                x.Matches,
                eloOf(x.Player1Id)!.Value + eloOf(x.Player2Id)!.Value,
                Math.Min(x.Player1Id, x.Player2Id)))
            .FirstOrDefault();
        if (topPair != null)
            result[Constants.Seasons.AwardCategories.Pair] = new LadderTop(
                Math.Min(topPair.Player1Id, topPair.Player2Id),
                Math.Max(topPair.Player1Id, topPair.Player2Id));

        return result;
    }
}
