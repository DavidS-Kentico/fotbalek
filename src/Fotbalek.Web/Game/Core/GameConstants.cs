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
    /// <summary>Distance between adjacent figure centers. Defaults to <c>(tableHeight − 2·WallMargin)
    /// / figureCount</c>, the spacing where every rod sweeps its inset coverage band with zero overlap
    /// and zero dead lanes (the <see cref="GameConstants.WallMargin"/> off each wall leaves room for
    /// the rod's end-stop collars); a smaller override packs the figures closer (defenders) and grows
    /// the travel to compensate.</summary>
    public double FigureSpacing => SpacingOverride ?? (GameConstants.TableHeight - 2 * GameConstants.WallMargin) / FigureCount;

    /// <summary>Vertical slide range of the rod. Defaults to the value that, with the spacing above,
    /// makes the figures span the inset coverage band (table height minus a <see cref="GameConstants.WallMargin"/>
    /// at each wall).</summary>
    public double Travel => TravelOverride ?? (GameConstants.TableHeight - 2 * GameConstants.WallMargin) - (FigureCount - 1) * FigureSpacing;

    /// <summary>Center y of figure 0 at rod offset 0. Equals <see cref="GameConstants.WallMargin"/>
    /// for the standard inset rods; a rod whose figures span less than the table (a short-travel
    /// goalie) is centered so its range straddles mid-goal.</summary>
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
    public const double FigureRadius = 18;

    /// <summary>Wall margin: outfield figures stay this far off the top/bottom side walls — their
    /// centres sweep [<c>WallMargin</c>, <c>TableHeight − WallMargin</c>] instead of reaching the
    /// walls. This leaves room on every rod for the sliding end-stop collars (client-drawn rod
    /// hardware) so all rods read like a real moving rod, not just the goalie. It also *is* the
    /// visible gap between the outermost figure and its collar (the collar rides <c>WallMargin</c>
    /// from that figure), so it's sized to leave a clear space, not glue the collar to the man.
    /// Still small enough that a figure's contact radius (ball 14 + figure 18 = 32) reaches a
    /// wall-hugging ball from its inset limit (30 − 14 = 16 ≪ 32), so no new dead lane opens along
    /// the walls — blocking coverage is unchanged. The goalie ignores this (its own short
    /// <c>TravelOverride</c> puts its collars far from the keeper already).</summary>
    public const double WallMargin = 30;

    public const int TickRate = 60;
    public const double FixedDt = 1.0 / TickRate;
    public const int TicksPerSnapshot = 2; // 30 Hz — higher rate lets the client run a tighter
                                           // interpolation delay (see INTERP_DELAY_MS in game.js)
                                           // for less ball lag at the same jitter safety.

    public const double RodSpeed = 650;

    /// <summary>Rod acceleration ramp (world units/s²): a held direction eases the rod up to
    /// <see cref="RodSpeed"/> over ~<c>RodSpeed/RodAccel</c> s instead of snapping to full speed. This
    /// makes a quick tap a small precise nudge and a sustained hold a full sweep — analog control from
    /// digital keys, and it also makes <see cref="SimState.RodVel"/> (hence shot power/english) rise
    /// with how long you've been sliding. Mirrored exactly by the client predictor, so keep them in sync.</summary>
    public const double RodAccel = 6500;

    /// <summary>Rod deceleration (world units/s²) when the key is released or the direction reversed —
    /// much snappier than <see cref="RodAccel"/> so stopping and blocking stay reactive and the rod
    /// never coasts past where you let go (a real foosball rod stops with your hand).</summary>
    public const double RodDecel = 13000;

    /// <summary>Dash burst speed (§skill-dash): tapping SPACE with no held ball shoves the seat's
    /// *moving* rods to this speed in one shot — a quick lunge to reach a shot you'd otherwise miss, or,
    /// struck into a loose ball, a hard angled shot (the kick reads live rod velocity, so a dashing
    /// figure fires much harder than a normal slide). No new integrator path: the impulse just seeds
    /// <see cref="SimState.RodVel"/>, then decays back to <see cref="RodSpeed"/> through the ordinary
    /// ramp (<see cref="GamePhysics.IntegrateRods"/>), and the client mirrors it by seeding the same
    /// velocity — so it rides the existing prediction machinery unchanged. Kept below the tunneling
    /// budget: <c>(MaxBallSpeed + DashSpeed)/TickRate</c> ≈ 58 stays under the 64-unit outfield contact
    /// diameter, so a shot can't step past a dashing defender (§4.6 sanity check).</summary>
    public const double DashSpeed = 1800;

    /// <summary>Shared per-player cooldown between dashes (§skill-dash): one dash, then a beat before the
    /// next, so the dash is a committed timing move rather than simply a faster way to travel.</summary>
    public const double DashCooldownSeconds = 1.0;
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

    /// <summary>One-timer power bonus (§skill-onetimer): a lane pass struck *first-time* — the catch key
    /// released before the passed ball settled on the new man — fires at <c>KickSpeed × (1 + this)</c>
    /// with no charge needed (the pass zeroes the charge, and a one-timer never rebuilds it). Set above
    /// <see cref="KickPowerBonus"/> so a well-timed one-touch finish is faster than a normal charged
    /// outfield shot (700×1.8 ≈ 1260 u/s — a clear reward for the timing), yet still short of the goalie
    /// cannon and the <see cref="MaxBallSpeed"/> cap, so it stays reactable. English still comes from the
    /// rod's slide at contact, so a one-timer can curve.</summary>
    public const double OneTimerPowerBonus = 0.8;

    /// <summary>Rod-lift drop-slam charge window (§skill-lift): the seconds of holding a rod's men *up*
    /// that build a full-power slam. When the men come back down onto the ball the strike ramps from a
    /// plain <see cref="KickSpeed"/> (a quick lift-and-drop) to
    /// <c>KickSpeed × (1 + <see cref="LiftSlamPowerBonus"/>)</c> at this much lift time — hold the men up
    /// longer, slam harder. Mirrors the goalie's dial-able charge, but paid in lift time instead of SPACE.</summary>
    public const double LiftSlamMaxCharge = 0.8;

    /// <summary>Power bonus of a fully-charged drop-slam (§skill-lift), as a fraction of
    /// <see cref="KickSpeed"/>. At 1.4 a full slam is 700×2.4 ≈ 1680 u/s — just under the
    /// <see cref="MaxBallSpeed"/> cap, a genuine cannon that still stays (barely) reactable.</summary>
    public const double LiftSlamPowerBonus = 1.4;

    /// <summary>Slam window (§skill-lift): once a lifted rod's men come down, a figure that catches the
    /// ball within this long fires the charged slam instead of an ordinary auto-kick. Wide enough that
    /// the timing isn't frame-perfect, tight enough that it stays a deliberate "drop onto the ball" move
    /// rather than a lingering power buff on an ordinary block.</summary>
    public const double LiftSlamWindowSeconds = 0.12;

    /// <summary>Fraction of the rod's vertical velocity added to the ball on a kick ("english").
    /// Higher = sliding the rod as it strikes curves the shot more — the main aim-by-motion lever.</summary>
    public const double RodMomentumTransfer = 0.55;

    /// <summary>Magnus curve strength: the perpendicular acceleration applied to a moving ball is this
    /// × <see cref="SimState.BallSpin"/> × its speed (units/s²). Higher = shots bend more. The main
    /// "curve the ball" lever; 0 disables curving entirely (shots fly straight, as before).</summary>
    public const double MagnusCoefficient = 0.30;

    /// <summary>How fast ball spin bleeds off (per second, exponential). A shot curves hardest just
    /// after the strike and straightens out — a brisk decay reads as a natural bend, not an endless
    /// spiral.</summary>
    public const double SpinDecayPerSecond = 1.1;

    /// <summary>Spin imparted by a kick from a rod at full <see cref="RodSpeed"/> — the classic
    /// "english" from sliding the rod as it strikes. Scales linearly with rod velocity at contact, so
    /// a still block imparts none and a committed swipe curves hard.</summary>
    public const double KickSpinFromRod = 1.0;

    /// <summary>Extra spin from an off-center contact (offset -1..1), added to the rod-motion spin:
    /// clipping the ball high or low bends it too, even from a still figure.</summary>
    public const double KickSpinFromOffset = 0.30;

    /// <summary>Hard cap on |<see cref="SimState.BallSpin"/>| so a stacked kick can't produce an
    /// absurd curve.</summary>
    public const double MaxSpin = 2.0;

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

    /// <summary>Half-width of the goalie's aim cone (§skill-aim), radians. While the keeper charges a
    /// caught shot (holding SPACE) the rod freezes and ↑/↓ swing the launch angle within ±this off
    /// straight-ahead; kept well under 90° so the cannon always fires forward, never back into its own
    /// net. ~60° is plenty to pick a corner over the long run to the far goal.</summary>
    public const double GoalieAimMaxAngle = 1.05;

    /// <summary>How fast ↑/↓ swing the goalie's aim (§skill-aim), radians/s. A full edge-to-edge sweep
    /// of the ±<see cref="GoalieAimMaxAngle"/> cone takes 2·1.05/1.9 ≈ 1.1 s — a touch quicker than the
    /// full charge window, so the keeper can snap onto a corner and still have charge headroom to spare.</summary>
    public const double GoalieAimRate = 1.9;

    /// <summary>Speed of a back-pass toss (§skill): brisk enough to reach the rod behind, slow enough
    /// that an opponent rod standing in the lane can step in and pick it off.</summary>
    public const double BackPassSpeed = 550;

    /// <summary>How fast a trapped ball follows its figure (units/s). Above <see cref="RodSpeed"/> so
    /// dribbling tracks tightly, but finite so a lane pass (SPACE) visibly slides the ball one man
    /// along the rod instead of teleporting.</summary>
    public const double TrapFollowSpeed = 900;

    /// <summary>Goalie collision radius — noticeably larger than the outfield <see cref="FigureRadius"/>
    /// so the last line is the most forgiving to hit/catch (contact = ball 14 + this = 38 vs 32 outfield).</summary>
    public const double GoalieRadius = 24;

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

    /// <summary>How long before an anti-stall reset (§2.5) the client starts warning: a ring tightens and
    /// reddens around the ball over this final window, so a stall re-center never comes out of nowhere.</summary>
    public const double StallWarningSeconds = 2;

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
