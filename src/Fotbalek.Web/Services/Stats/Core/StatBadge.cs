namespace Fotbalek.Web.Services.Stats.Core;

/// <summary>
/// Display metadata when a stat opts into being rendered as an inline badge next to a player's name.
/// </summary>
public record StatBadge(string IconClass, string CssClass);
