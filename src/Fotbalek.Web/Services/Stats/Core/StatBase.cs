namespace Fotbalek.Web.Services.Stats.Core;

/// <summary>
/// Convenience base for stat implementations: declare metadata once, override Compute(context) to produce holders.
/// </summary>
public abstract class StatBase : IStat
{
    public abstract string Key { get; }
    public abstract string Name { get; }
    public abstract string Emoji { get; }
    public abstract StatTheme Theme { get; }
    public abstract string Description { get; }
    public virtual StatBadge? Badge => null;

    public virtual bool Applies(StatContext context) => true;

    public StatResult Calculate(StatContext context) =>
        new(Key, Name, Emoji, Theme, Description, Compute(context), Badge);

    protected abstract IReadOnlyList<StatHolder> Compute(StatContext context);
}
