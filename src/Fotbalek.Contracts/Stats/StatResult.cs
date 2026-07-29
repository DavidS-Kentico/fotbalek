namespace Fotbalek.Contracts.Stats;

/// <summary>
/// The output of a stat calculation. Holders can be empty when the stat does not apply to the current context.
/// Data only — the name, wording, emoji and badge styling that belong to <see cref="Key"/> live in the UI.
/// </summary>
public record StatResult(
    StatKey Key,
    StatTheme Theme,
    IReadOnlyList<StatHolder> Holders);
