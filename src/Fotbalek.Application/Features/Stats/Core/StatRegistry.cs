using Fotbalek.Contracts.Stats;
namespace Fotbalek.Application.Features.Stats.Core;

/// <summary>
/// Holds every IStat implementation in the system and computes their results against a context.
/// </summary>
public class StatRegistry(IEnumerable<IStat> stats)
{
    private readonly IReadOnlyList<IStat> _stats = stats.ToList();

    /// <summary>Compute every applicable stat for the context. Stats whose <c>Applies(ctx)</c> returns false are skipped entirely.</summary>
    public List<StatResult> ComputeAll(StatContext context) =>
        _stats.Where(s => s.Applies(context)).Select(s => s.Calculate(context)).ToList();

    /// <summary>Returns the badges a single player holds, formatted for inline rendering.</summary>
    public static List<PlayerBadge> PlayerBadges(IEnumerable<StatResult> results, int playerId) =>
        results
            .Where(r => r.Badge != null)
            .Select(r => new
            {
                Result = r,
                Holder = r.Holders.FirstOrDefault(h => h.PlayerId == playerId)
            })
            .Where(x => x.Holder != null)
            .Select(x => new PlayerBadge(
                IconClass: x.Result.Badge!.IconClass,
                CssClass: x.Result.Badge.CssClass,
                Tooltip: $"{x.Result.Name} - {x.Holder!.DisplayValue}"))
            .ToList();
}
