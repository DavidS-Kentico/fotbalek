namespace Fotbalek.Web.Game.Core;

/// <summary>
/// Seat identifiers and the hand→rod mapping (§2.2). Seats mirror real doubles foosball;
/// hands are screen-relative: hand 0 = left (<c>W</c>/<c>S</c>) drives the seat's rod(s)
/// nearer the left edge of the screen, hand 1 = right (arrows) the rod(s) nearer the right —
/// so for side B the left hand drives the offensive rods. Mirrored by game.js for
/// own-rod prediction.
/// </summary>
public static class SeatMap
{
    public const int ADefense = 0;
    public const int AAttack = 1;
    public const int BDefense = 2;
    public const int BAttack = 3;
    public const int SeatCount = 4;

    public const int LeftHand = 0;
    public const int RightHand = 1;

    public static readonly string[] SeatNames = ["A-Defense", "A-Attack", "B-Defense", "B-Attack"];

    /// <summary>0 = side A, 1 = side B.</summary>
    public static int SideOf(int seat) => seat <= AAttack ? 0 : 1;

    // [seat][hand] → rod index, when both seats of the side are occupied.
    // Ordered left-on-screen first, so hand 0 (W/S) is always the leftmost of the pair.
    private static readonly int[][] OwnRods =
    [
        [0, 1], // A-Defense: GK (x75), DEF (x225)
        [3, 5], // A-Attack:  MID (x525), ATK (x825)
        [6, 7], // B-Defense: DEF (x975), GK (x1125)
        [2, 4], // B-Attack:  ATK (x375), MID (x675)
    ];

    // [side][hand] → rods, when a lone player drives the whole side (1v1 pairing, §2.2).
    // Pairs stay role-based (GK+DEF and MID+ATK); the hand takes the pair nearer its side
    // of the screen — for side B the left hand drives the offensive pair.
    private static readonly int[][][] PairRods =
    [
        [[0, 1], [3, 5]], // side A: left = GK+DEF, right = MID+ATK
        [[2, 4], [6, 7]], // side B: left = ATK+MID, right = DEF+GK
    ];

    /// <summary>Rods driven by a hand of the given seat. <paramref name="alone"/> = the seat's
    /// occupant is the only player seated on their side.</summary>
    public static int[] RodsFor(int seat, int hand, bool alone) =>
        alone ? PairRods[SideOf(seat)][hand] : [OwnRods[seat][hand]];

    /// <summary>All rods belonging to a side, defensive to offensive.</summary>
    public static int[] SideRods(int side) => side == 0 ? [0, 1, 3, 5] : [7, 6, 4, 2];
}
