namespace Fotbalek.Contracts.Stats;

/// <summary>One inline badge a player holds, formatted for rendering next to their name.</summary>
public record PlayerBadge(string IconClass, string CssClass, string Tooltip);
