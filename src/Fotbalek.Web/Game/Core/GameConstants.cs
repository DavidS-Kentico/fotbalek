namespace Fotbalek.Web.Game.Core;

/// <summary>
/// One rod on the table. <c>Side</c>: 0 = team A (attacks +x, goal on the left),
/// 1 = team B (attacks -x, goal on the right).
/// </summary>
/// <param name="SpacingOverride">Explicit figure spacing; null = the default tableHeight/figureCount.</param>
/// <param name="TravelOverride">Explicit vertical slide range; null = the default that makes the rod
/// sweep the full table height. A shorter travel (goalie) covers only part of the height and is
/// auto-centered via <see cref="YBase"/>.</param>
/// <param name="Radius">Collision radius of each figure on this rod.</param>
public sealed record RodDef(
    int Index, double X, int Side, int FigureCount, string Role,
    double? SpacingOverride = null, double? TravelOverride = null,
    double Radius = GameConstants.FigureRadius)
{
    /// <summary>Distance between adjacent figure centers. Defaults to tableHeight / figureCount, the
    /// spacing where every rod sweeps the full table height with zero overlap and zero dead lanes;
    /// a smaller override packs the figures closer (defenders) and grows the travel to compensate.</summary>
    public double FigureSpacing => SpacingOverride ?? GameConstants.TableHeight / FigureCount;

    /// <summary>Vertical slide range of the rod. Defaults to the value that, with the spacing above,
    /// makes the figures span the full table height.</summary>
    public double Travel => TravelOverride ?? GameConstants.TableHeight - (FigureCount - 1) * FigureSpacing;

    /// <summary>Center y of figure 0 at rod offset 0. Zero for full-height rods; a rod whose figures
    /// span less than the table (a short-travel goalie) is centered so its range straddles mid-goal.</summary>
    public double YBase => (GameConstants.TableHeight - (Travel + (FigureCount - 1) * FigureSpacing)) / 2;

    /// <summary>Center y of figure <paramref name="i"/> (0 = topmost) at rod offset 0..1.</summary>
    public double FigureY(double offset, int i) => YBase + offset * Travel + i * FigureSpacing;

    /// <summary>Rod offset 0..1 (clamped) that lands figure <paramref name="i"/> nearest world y.</summary>
    public double OffsetForFigureY(double y, int i) =>
        Math.Clamp((y - YBase - i * FigureSpacing) / Travel, 0, 1);
}

/// <summary>
/// Table geometry and physics tuning (spec §4.6). All lengths in logical table units,
/// speeds in units/second. No server-only dependencies — shared with a future WASM client as-is.
/// </summary>
public static class GameConstants
{
    public const double TableWidth = 1200;
    public const double TableHeight = 700;
    public const double GoalMouthHeight = 230;
    public const double BallRadius = 14;
    public const double FigureRadius = 16;

    public const int TickRate = 60;
    public const double FixedDt = 1.0 / TickRate;
    public const int TicksPerSnapshot = 3; // 20 Hz

    public const double RodSpeed = 650;
    // Base speed of a plain shot — what a *still* figure imparts when the ball just rolls into it.
    // A figure that is moving when it strikes adds up to KickPowerBonus×KickSpeed on top (see
    // GamePhysics.FireShot), so a committed "hit it on the move" shot flies harder than a passive
    // deflection: power is a skill lever, not a constant. Kept below MaxBallSpeed so a full shot
    // stays (barely) reactable.
    public const double KickSpeed = 700;
    public const double MaxDeflection = 500;

    /// <summary>Extra horizontal shot speed as a fraction of <see cref="KickSpeed"/>, granted at full
    /// rod speed (auto-kick) or full charge (trap-shot). 0 = every shot the same fixed speed (the old
    /// behavior); at 0.4 a fully committed strike is 40% faster than a dead block.</summary>
    public const double KickPowerBonus = 0.4;

    /// <summary>Fraction of the rod's vertical velocity added to the ball on a kick ("english").
    /// Higher = sliding the rod as it strikes curves the shot more — the main aim-by-motion lever.</summary>
    public const double RodMomentumTransfer = 0.55;

    public const double KickCooldownSeconds = 0.13;
    public const double MaxBallSpeed = 1400;
    public const double FrictionPerSecond = 0.3;
    public const double WallRestitution = 0.85;
    public const double KickoffSpeed = 250;

    /// <summary>Trap-shot charge time: hold the kick key to catch the ball on a figure; shot power
    /// ramps from a soft pass at 0 to a full cannon over this long, then holds at full. Aiming is by
    /// the slide direction at release (pull/push), so a quick tap places, a full hold blasts.</summary>
    public const double MaxChargeSeconds = 0.6;

    /// <summary>Trap hold window: a trapped ball auto-fires after this long even while the key stays
    /// held, so nobody can freeze the game by hoarding it — the untouched-15s stall guard can't see a
    /// held ball (it reads as continuously touched). Long enough to catch, reposition (SPACE) and
    /// aim, short enough that stalling isn't a tactic.</summary>
    public const double TrapTimeoutSeconds = 2.0;

    /// <summary>How fast a trapped ball follows its figure (units/s). Above <see cref="RodSpeed"/> so
    /// dribbling tracks tightly, but finite so a lane pass (SPACE) visibly slides the ball one man
    /// along the rod instead of teleporting.</summary>
    public const double TrapFollowSpeed = 900;

    /// <summary>Goalie collision radius — a touch larger than the outfield <see cref="FigureRadius"/>
    /// so the last line is a little more forgiving to hit.</summary>
    public const double GoalieRadius = 20;

    /// <summary>Goalie vertical slide range (&lt; <see cref="TableHeight"/>): the single goalie covers
    /// only the goal area, auto-centered, instead of sweeping the whole table — easier to focus on.</summary>
    public const double GoalieTravel = 300;

    /// <summary>Figure spacing on the 2-man defense rods — closer than the default half-table, like a
    /// real table; travel grows to keep full vertical coverage.</summary>
    public const double DefenderSpacing = 260;

    /// <summary>Goals scored within this long of a round going live don't count (kickoff redoes) when
    /// the room's <see cref="GameOptionsDto.DisallowQuickGoals"/> is on — no cheap kickoff goals.</summary>
    public const double QuickGoalGraceSeconds = 1.5;

    public const double StallSpeedThreshold = 40;
    public const double StallSpeedSeconds = 5;
    public const double StallUntouchedSeconds = 15;

    /// <summary>Pause before the ball (re)starts moving: after a goal, on entering playing
    /// from waiting, and after a reconnect resume (§2.4, §3.4).</summary>
    public const double KickoffPauseSeconds = 1.0;

    public const int WinningScore = 10;

    public const double SeatGraceSeconds = 30;
    public const double EmptyRoomTimeoutSeconds = 120;

    public static double GoalMouthTop => (TableHeight - GoalMouthHeight) / 2;
    public static double GoalMouthBottom => (TableHeight + GoalMouthHeight) / 2;

    /// <summary>Standard doubles table, left goal (A) to right goal (B): rods evenly spaced
    /// 150 apart, GK rods 75 from their goal line; 1 GK, 2 DEF, 5 MID, 3 ATK per team (§2.1).
    /// The goalie has a shorter, goal-centered travel and a slightly larger collider; the defense
    /// pair sits closer together than the default half-table spacing.</summary>
    public static readonly IReadOnlyList<RodDef> Rods =
    [
        new(0, 75, 0, 1, "GK", TravelOverride: GoalieTravel, Radius: GoalieRadius),
        new(1, 225, 0, 2, "DEF", SpacingOverride: DefenderSpacing),
        new(2, 375, 1, 3, "ATK"),
        new(3, 525, 0, 5, "MID"),
        new(4, 675, 1, 5, "MID"),
        new(5, 825, 0, 3, "ATK"),
        new(6, 975, 1, 2, "DEF", SpacingOverride: DefenderSpacing),
        new(7, 1125, 1, 1, "GK", TravelOverride: GoalieTravel, Radius: GoalieRadius),
    ];
}
