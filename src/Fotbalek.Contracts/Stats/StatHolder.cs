namespace Fotbalek.Contracts.Stats;

/// <summary>
/// One player who holds a stat result. Multiple holders for ties; pair stats use <see cref="Detail"/>
/// to name the partner/opponent.
/// </summary>
/// <param name="Value">The ranked metric — what the stat sorts holders by.</param>
/// <param name="Detail">The other player a pair/rivalry stat is about, when there is one.</param>
/// <param name="Ratio">The operands behind <paramref name="Value"/>, for the stats that have any.</param>
public record StatHolder(
    int PlayerId,
    string PlayerName,
    int AvatarId,
    int Value,
    string? Detail = null,
    StatRatio? Ratio = null);

/// <summary>
/// The two numbers a holder's value was derived from — e.g. 7 wins out of 10 games, 3 of 8 possible
/// teammates, or a current rating of 1180 against a 1290 peak. The UI decides how to word them.
/// </summary>
public record StatRatio(int Part, int Whole);
