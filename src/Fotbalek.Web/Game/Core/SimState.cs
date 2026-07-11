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

    /// <summary>Ball spin ("english"), signed and roughly in -<see cref="GameConstants.MaxSpin"/>..
    /// +<see cref="GameConstants.MaxSpin"/>. A spinning ball curves in flight: <see cref="GamePhysics"/>
    /// applies a Magnus acceleration perpendicular to its velocity each step, and the spin bleeds off
    /// over time. Imparted by a kick from a *moving* rod (and off-center contact); zeroed on a
    /// caught/parked ball. Carried in snapshots so the client can show the ball visibly spinning.</summary>
    public double BallSpin;

    /// <summary>While true the ball is not integrated (waiting, game over, kickoff pause).
    /// Rods keep moving so players can position while frozen.</summary>
    public bool BallFrozen = true;

    /// <summary>Rod slide positions, 0 (top) .. 1 (bottom).</summary>
    public readonly double[] RodOffset = [0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5];

    /// <summary>Rod velocity in units/s (0 when clamped at a wall) — feeds the rod-momentum kick
    /// component (§2.3). Also the persistent state of the acceleration ramp (<see cref="GameConstants.RodAccel"/>):
    /// <see cref="GamePhysics.IntegrateRods"/> reads it back each tick to continue easing the rod toward
    /// its held-direction target speed.</summary>
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

    /// <summary>Held-SPACE state per rod: true while the driving seat holds SPACE. Set by the room each
    /// tick. The goalie charges its shot only while this is held (hold to build power, release to
    /// launch) — its power is deliberate, not automatic (§skill). Ignored by outfield rods, which
    /// charge as they cradle the ball (see <see cref="RodKick"/>) and use SPACE only to pass.</summary>
    public readonly bool[] RodSpace = new bool[8];

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

    /// <summary>Seconds of shot power charged on the current trap — scales the shot speed (capped at
    /// the rod's max-charge window). Advances only while the rod's charge input is held (the catch key
    /// for outfield rods, SPACE for the goalie), so the goalie's power is player-driven. Reset when a
    /// trap ends or a lane pass hands the ball to a new man.</summary>
    public double ChargeSeconds;

    /// <summary>Seconds the ball has been trapped, regardless of charging — the auto-fire backstop that
    /// keeps nobody hoarding it (and feeds the draining hold ring). Separate from <see cref="ChargeSeconds"/>
    /// so a goalie who catches but doesn't charge still auto-fires. Reset with the trap / on a lane pass.</summary>
    public double HoldSeconds;

    /// <summary>Set for one tick when the trapping player taps pass (SPACE): the trapped ball hops to
    /// the adjacent figure on the same rod in the slide direction (§skill). Consumed by
    /// <see cref="GamePhysics"/>; cleared when the trap ends.</summary>
    public bool PassRequested;

    /// <summary>Armed on a lane pass, disarmed once the passed ball settles on the new man while still
    /// held (the player has taken control). If instead the catch is *released before it settles*, the
    /// shot that fires on arrival is a one-timer (§skill-onetimer): a first-time strike off the pass,
    /// fired at a fixed high power (<see cref="GameConstants.OneTimerPowerBonus"/>) with no charge —
    /// rewarding the timing. Outfield only; the goalie can't lane-pass. Server-only — the ball trajectory
    /// rides the normal snapshot stream, so no wire or prediction change.</summary>
    public bool OneTimerArmed;

    /// <summary>Eased y of a trapped ball. It follows the target figure quickly enough that dribbling
    /// tracks tightly, but a lane pass slides the ball one man along instead of teleporting. Set to the
    /// figure's y when a trap begins.</summary>
    public double TrapY;

    /// <summary>Aim angle of a trapped goalie's charged shot (§skill-aim), radians off straight-ahead
    /// (0 = straight at the opponent goal, + = toward the bottom wall, − = toward the top), clamped to
    /// ±<see cref="GameConstants.GoalieAimMaxAngle"/>. Steered by ↑/↓ while the keeper charges (SPACE
    /// held, which also freezes the rod); the cannon fires dead straight along it. Reset when a trap
    /// begins or ends. Ignored by outfield rods (they aim by the release slide). Carried in snapshots so
    /// every client can draw the launch arrow and read the shot.</summary>
    public double AimAngle;

    /// <summary>Most recent auto-kick, carried in snapshots so the client can play the
    /// figure's swing animation in sync with the tick timeline (§4.3). -1 = none yet.</summary>
    public int LastKickRod = -1;
    public int LastKickFigure = -1;
    public long LastKickTick;

    /// <summary>Tick of the most recent pass (a lane-pass hop between own figures or a back-pass toss) —
    /// carried in snapshots purely so the client can play a distinct pass sound. Separate from the kick
    /// channel because a pass fires no forward swing: setting <see cref="LastKickTick"/> would animate
    /// one. 0 = none yet.</summary>
    public long LastPassTick;

    public long Tick;

    /// <summary>Incremented on any ball teleport so clients snap instead of interpolating
    /// across the discontinuity (§4.3). Owned by the room.</summary>
    public int ResetCounter;

    public double BallSpeed => Math.Sqrt(BallVX * BallVX + BallVY * BallVY);
}
