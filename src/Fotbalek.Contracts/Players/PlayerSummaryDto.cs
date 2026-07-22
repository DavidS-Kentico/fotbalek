namespace Fotbalek.Contracts.Players;

/// <summary>Per-player roster summary on the current lens (seasonal or all-time).</summary>
public record PlayerSummaryDto(
    int Games,
    int Wins,
    int Losses,
    DateTimeOffset? LastPlayedAt,
    int LastEloChange)
{
    public double WinRate => Games > 0 ? Wins * 100.0 / Games : 0;
}
