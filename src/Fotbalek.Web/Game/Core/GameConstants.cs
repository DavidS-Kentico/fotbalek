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
    // Global speed cap. Raised above the old 1400 to give the goalie's full-charge cannon real headroom
    // (see GoalieCaughtPowerBonus). Still well under the ~2500 where the fixed step would need
    // substepping to avoid tunneling (§4.6 sanity check).
    public const double MaxBallSpeed = 1700;
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

    /// <summary>Trap hold window for the goalie — longer than the outfield <see cref="TrapTimeoutSeconds"/>
    /// so the keeper can catch, line up and launch a clearance (§skill). Still bounded, so it can't be
    /// used to stall.</summary>
    public const double GoalieTrapTimeoutSeconds = 5.0;

    /// <summary>The goalie's *caught*-shot power bonus at full charge (§skill). A goalie trap-shot ramps
    /// from plain <see cref="KickSpeed"/> (a quick tap → regular strength) up to
    /// <c>KickSpeed × (1 + this)</c> at full charge; at 1.35 that's ~1645 u/s — just under the raised
    /// <see cref="MaxBallSpeed"/> cap, so the full charge is a genuine cannon rather than a clamped 1400.
    /// Only the caught shot gets it — an uncaught goalie auto-kick stays on the ordinary
    /// <see cref="KickPowerBonus"/>.</summary>
    public const double GoalieCaughtPowerBonus = 1.35;

    /// <summary>How long a goalie charges to full shot power (§skill) — longer than the snappy outfield
    /// <see cref="MaxChargeSeconds"/> so the keeper can dial the strength in and read it off the power
    /// ring before launching. Well within the 5 s hold window.</summary>
    public const double GoalieMaxChargeSeconds = 1.5;

    /// <summary>Speed of a back-pass toss (§skill): brisk enough to reach the rod behind, slow enough
    /// that an opponent rod standing in the lane can step in and pick it off.</summary>
    public const double BackPassSpeed = 550;

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

    /// <summary>Most figure contacts a round can have and still have its goal waved off as a "first
    /// touch" (0 = served straight in, 1 = a one-touch finish) when the room's
    /// <see cref="GameOptionsDto.DisallowFirstTouchGoals"/> is on. A ball that was deliberately
    /// trapped/controlled is exempt regardless of the contact count (see <c>GameRoom</c>), so this
    /// only nixes uncontrolled deflections that beat the <see cref="QuickGoalGraceSeconds"/> window.</summary>
    public const int FirstTouchMaxContacts = 1;

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

    private static bool IsGoalie(int rodIndex) => Rods[rodIndex].Role == "GK";

    /// <summary>The hold window before a trapped ball auto-fires, per rod — the goalie holds longer
    /// (§skill).</summary>
    public static double TrapTimeout(int rodIndex) =>
        IsGoalie(rodIndex) ? GoalieTrapTimeoutSeconds : TrapTimeoutSeconds;

    /// <summary>Seconds to charge a trapped shot to full power, per rod — the goalie ramps slower so
    /// the strength is dial-able (§skill).</summary>
    public static double MaxCharge(int rodIndex) =>
        IsGoalie(rodIndex) ? GoalieMaxChargeSeconds : MaxChargeSeconds;

    /// <summary>Power bonus of a *caught* shot at full charge, per rod — the goalie's big ramp to a
    /// near-max cannon vs the ordinary <see cref="KickPowerBonus"/> for everyone else (§skill).</summary>
    public static double CaughtPowerBonus(int rodIndex) =>
        IsGoalie(rodIndex) ? GoalieCaughtPowerBonus : KickPowerBonus;

    /// <summary>The same-side rod one step toward its own goal (behind <paramref name="rodIndex"/>),
    /// or -1 if none — the goalie has nothing behind it. Target of a back-pass (§skill). Side A
    /// defends the left goal (x=0), side B the right (x=Width), so "behind" is the nearer same-side
    /// rod on the goal side.</summary>
    public static int RodBehind(int rodIndex)
    {
        var rod = Rods[rodIndex];
        var best = -1;
        var bestX = rod.Side == 0 ? double.NegativeInfinity : double.PositiveInfinity;
        foreach (var other in Rods)
        {
            if (other.Side != rod.Side || other.Index == rodIndex)
                continue;
            var isBehind = rod.Side == 0 ? other.X < rod.X : other.X > rod.X;
            var isCloser = rod.Side == 0 ? other.X > bestX : other.X < bestX;
            if (isBehind && isCloser)
            {
                bestX = other.X;
                best = other.Index;
            }
        }
        return best;
    }
}
