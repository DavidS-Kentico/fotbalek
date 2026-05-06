namespace Fotbalek.Web.Services.Stats.Core;

/// <summary>
/// One self-contained stat: knows its identity, theme, optional badge metadata, and how to compute its result.
/// </summary>
public interface IStat
{
    string Key { get; }
    string Name { get; }
    string Emoji { get; }
    StatTheme Theme { get; }
    string Description { get; }

    /// <summary>Optional inline-badge presentation. When non-null, the stat is rendered everywhere badges are shown.</summary>
    StatBadge? Badge { get; }

    /// <summary>Compute the stat. Return a result with empty Holders when the stat does not apply to the given context.</summary>
    StatResult Calculate(StatContext context);
}
