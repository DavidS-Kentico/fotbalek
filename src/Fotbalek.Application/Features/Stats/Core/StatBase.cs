using Fotbalek.Contracts.Stats;
namespace Fotbalek.Application.Features.Stats.Core;

/// <summary>
/// Convenience base for stat implementations: declare key and theme once, override Compute(context)
/// to produce holders.
/// </summary>
public abstract class StatBase : IStat
{
    public abstract StatKey Key { get; }
    public abstract StatTheme Theme { get; }

    public virtual bool Applies(StatContext context) => true;

    public StatResult Calculate(StatContext context) => new(Key, Theme, Compute(context));

    protected abstract IReadOnlyList<StatHolder> Compute(StatContext context);
}
