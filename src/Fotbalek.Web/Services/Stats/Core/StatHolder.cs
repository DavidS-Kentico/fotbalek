namespace Fotbalek.Web.Services.Stats.Core;

/// <summary>
/// One player who holds a stat result. Multiple holders for ties; pair stats use Detail to name the partner/opponent.
/// </summary>
public record StatHolder(
    int PlayerId,
    string PlayerName,
    int AvatarId,
    int Value,
    string DisplayValue,
    string? Detail = null);
