namespace Fotbalek.Web.Game.Core;

/// <summary>
/// Mutable simulation state for one table. Plain data — all rules live in
/// <see cref="GamePhysics"/>; ownership/lifecycle live in the room.
/// </summary>
public sealed class SimState
{
    public double BallX = GameConstants.TableWidth / 2;
    public double BallY = GameConstants.TableHeight / 2;
    public double BallVX;
    public double BallVY;

    /// <summary>While true the ball is not integrated (waiting, game over, kickoff pause).
    /// Rods keep moving so players can position while frozen.</summary>
    public bool BallFrozen = true;

    /// <summary>Rod slide positions, 0 (top) .. 1 (bottom).</summary>
    public readonly double[] RodOffset = [0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5];

    /// <summary>Actual rod velocity in units/s from the last step (0 when clamped at a wall) —
    /// feeds the rod-momentum kick component (§2.3).</summary>
    public readonly double[] RodVel = new double[8];

    /// <summary>Held-input direction per rod: -1 up, 0 stop, +1 down. Set by the room each tick
    /// from seated players' hand state.</summary>
    public readonly int[] RodDir = new int[8];

    /// <summary>When set, the rod glides back toward center offset 0.5 at rod speed instead of
    /// following <see cref="RodDir"/> — used while its whole side is unseated (§2.2).</summary>
    public readonly bool[] RodGlide = new bool[8];

    /// <summary>Held-kick state per rod: true while the seat's kick key for that rod is down. Set by
    /// the room each tick alongside <see cref="RodDir"/>. A held kick traps the ball on the figure it
    /// contacts (charging a shot) instead of the figure auto-kicking it.</summary>
    public readonly bool[] RodKick = new bool[8];

    /// <summary>Remaining kick cooldown per figure, seconds; indexed [rod][figure].</summary>
    public readonly double[][] KickCooldown =
        GameConstants.Rods.Select(r => new double[r.FigureCount]).ToArray();

    /// <summary>The figure that last kicked the ball; its collider is ignored until the two
    /// separate so a kick from behind passes through cleanly (§2.3). -1 = none.</summary>
    public int IgnoreRod = -1;
    public int IgnoreFigure = -1;

    /// <summary>Ball currently trapped (held) on this rod/figure while its owner charges a shot;
    /// -1 = not trapped. While trapped the ball rides the figure and normal integration is suspended
    /// (<see cref="GamePhysics"/>). Cleared whenever the ball is parked.</summary>
    public int TrappedRod = -1;
    public int TrappedFigure = -1;

    /// <summary>Seconds the current trap has been charging; scales shot power, capped at
    /// <see cref="GameConstants.MaxChargeSeconds"/>. Reset to 0 when a trap ends.</summary>
    public double ChargeSeconds;

    /// <summary>Set for one tick when the trapping player taps pass (SPACE): the trapped ball hops to
    /// the adjacent figure on the same rod in the slide direction (§skill). Consumed by
    /// <see cref="GamePhysics"/>; cleared when the trap ends.</summary>
    public bool PassRequested;

    /// <summary>Eased y of a trapped ball. It follows the target figure quickly enough that dribbling
    /// tracks tightly, but a lane pass slides the ball one man along instead of teleporting. Set to the
    /// figure's y when a trap begins.</summary>
    public double TrapY;

    /// <summary>Most recent auto-kick, carried in snapshots so the client can play the
    /// figure's swing animation in sync with the tick timeline (§4.3). -1 = none yet.</summary>
    public int LastKickRod = -1;
    public int LastKickFigure = -1;
    public long LastKickTick;

    public long Tick;

    /// <summary>Incremented on any ball teleport so clients snap instead of interpolating
    /// across the discontinuity (§4.3). Owned by the room.</summary>
    public int ResetCounter;

    public double BallSpeed => Math.Sqrt(BallVX * BallVX + BallVY * BallVY);
}
