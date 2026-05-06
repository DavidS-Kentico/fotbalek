namespace Fotbalek.Web.Services.Stats.Core;

/// <summary>
/// The output of a stat calculation. Holders can be empty when the stat does not apply to the current context.
/// </summary>
public record StatResult(
    string Key,
    string Name,
    string Emoji,
    StatTheme Theme,
    string Description,
    IReadOnlyList<StatHolder> Holders,
    StatBadge? Badge);
