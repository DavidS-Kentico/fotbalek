using Fotbalek.SharedKernel;
using Fotbalek.Application.Features.Stats.Core;
using Fotbalek.Contracts.Stats;

namespace Fotbalek.Application.Features.Stats.Activity;

/// <summary>
/// Rewards players whose games are spread most evenly across the currently active roster.
/// Uses normalized Shannon entropy (Pielou's evenness) of the partner-game distribution:
///   H = −Σ pᵢ · log(pᵢ),  evenness = H / log(K)
/// where K = active roster size − 1 (max possible partners). Result is in [0, 1] and is
/// maximized when the player has played equally with every active teammate.
/// </summary>
public class VarietyPlayerStat : StatBase
{
    public override string Key => "VarietyPlayer";
    public override string Name => "Variety Player";
    public override string Emoji => "\U0001F308";
    public override StatTheme Theme => StatTheme.Activity;
    public override string Description => $"Most even distribution of games across active teammates (min {Constants.TimeThresholds.MinGamesForVarietyBadge})";

    /// <summary>
    /// Computes Pielou's evenness for a player's partner-game distribution.
    /// </summary>
    /// <param name="gamesPerPartner">PartnerId → games played together (restricted to partners that should count).</param>
    /// <param name="rosterSizeExcludingSelf">Number of potential partners (active roster minus self) used as the normalization denominator.</param>
    /// <returns>Evenness in [0, 1]; 0 when there is no meaningful distribution.</returns>
    public static double ComputeEvenness(IReadOnlyDictionary<int, int> gamesPerPartner, int rosterSizeExcludingSelf)
    {
        if (rosterSizeExcludingSelf < 2) return 0; // entropy needs at least 2 possible partners

        var total = 0;
        foreach (var n in gamesPerPartner.Values) total += n;
        if (total == 0) return 0;

        double H = 0;
        foreach (var n in gamesPerPartner.Values)
        {
            if (n <= 0) continue;
            var p = (double)n / total;
            H -= p * Math.Log(p);
        }

        var maxH = Math.Log(rosterSizeExcludingSelf);
        return maxH > 0 ? Math.Clamp(H / maxH, 0.0, 1.0) : 0;
    }

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var activeIds = context.ActivePlayers.Select(p => p.Id).ToHashSet();
        if (activeIds.Count < 3) return []; // need self + ≥2 potential partners

        // Build per-player distribution of games with each currently-active teammate.
        var perPlayerPartners = new Dictionary<int, Dictionary<int, int>>();
        foreach (var match in context.Matches)
        {
            foreach (var team in new[] { 1, 2 })
            {
                var players = match.MatchPlayers.Where(mp => mp.TeamNumber == team).ToList();
                if (players.Count != 2) continue;
                Accumulate(perPlayerPartners, players[0].PlayerId, players[1].PlayerId, activeIds);
                Accumulate(perPlayerPartners, players[1].PlayerId, players[0].PlayerId, activeIds);
            }
        }

        var rosterMinusSelf = activeIds.Count - 1;
        var minGames = Constants.TimeThresholds.MinGamesForVarietyBadge;

        var scored = perPlayerPartners
            .Where(kv => activeIds.Contains(kv.Key)) // only rank active players
            .Select(kv => new
            {
                PlayerId = kv.Key,
                TotalPartnerGames = kv.Value.Values.Sum(),
                PartnersPlayed = kv.Value.Count,
                Evenness = ComputeEvenness(kv.Value, rosterMinusSelf)
            })
            .Where(x => x.TotalPartnerGames >= minGames && x.Evenness > 0)
            .ToList();

        if (scored.Count == 0) return [];

        var maxEvenness = scored.Max(s => s.Evenness);
        return scored
            .Where(s => Math.Abs(s.Evenness - maxEvenness) < 0.0001)
            .Select(s => context.PlayersById[s.PlayerId].ToHolder(
                (int)Math.Round(s.Evenness * 10000),
                $"{Math.Round(s.Evenness * 100)}% evenness",
                $"{s.PartnersPlayed}/{rosterMinusSelf} teammates"))
            .ToList();
    }

    private static void Accumulate(Dictionary<int, Dictionary<int, int>> map, int player, int partner, HashSet<int> activeIds)
    {
        // Only count games with currently-active partners; matches the denominator (active roster − 1) so evenness stays in [0,1].
        if (!activeIds.Contains(partner)) return;
        if (!map.TryGetValue(player, out var dict))
        {
            dict = new Dictionary<int, int>();
            map[player] = dict;
        }
        dict.TryGetValue(partner, out var n);
        dict[partner] = n + 1;
    }
}
