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

    /// <summary>
    /// Whether this stat is meaningful in the given context. When false, the stat is hidden entirely
    /// (not even greyed out). Use for stats that only make sense in all-time vs. filtered views.
    /// </summary>
    bool Applies(StatContext context);

    /// <summary>Compute the stat. Return a result with empty Holders when no player qualifies (greyed-out display).</summary>
    StatResult Calculate(StatContext context);
}
