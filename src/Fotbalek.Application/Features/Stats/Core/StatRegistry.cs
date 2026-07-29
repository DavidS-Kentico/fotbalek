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
}
