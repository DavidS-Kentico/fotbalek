using Fotbalek.Contracts.Stats;
namespace Fotbalek.Application.Features.Stats.Core;

/// <summary>
/// One self-contained stat: knows its key, its theme, and how to compute its result. Everything about
/// how the stat is *shown* — name, emoji, description, badge styling, the wording of a value — is the
/// UI's business and lives there, keyed by <see cref="Key"/>.
/// </summary>
public interface IStat
{
    StatKey Key { get; }
    StatTheme Theme { get; }

    /// <summary>
    /// Whether this stat is meaningful in the given context. When false, the stat is hidden entirely
    /// (not even greyed out). Use for stats that only make sense in all-time vs. filtered views.
    /// </summary>
    bool Applies(StatContext context);

    /// <summary>Compute the stat. Return a result with empty Holders when no player qualifies (greyed-out display).</summary>
    StatResult Calculate(StatContext context);
}
