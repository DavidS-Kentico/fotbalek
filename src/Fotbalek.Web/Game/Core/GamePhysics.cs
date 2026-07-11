namespace Fotbalek.Web.Game.Core;

/// <summary>Outcome of one fixed step. <c>GoalBySide</c>: -1 = none, 0 = team A scored
/// (ball fully crossed the right goal line), 1 = team B scored (left goal line).</summary>
public readonly record struct StepResult(int GoalBySide, bool Touched);

/// <summary>
/// Fixed-step integration, collisions, auto-kick and goal detection — pure functions over
/// <see cref="SimState"/> (§4.2). No server dependencies, unit-testable.
/// </summary>
public static class GamePhysics
{
    public static StepResult Step(SimState s, double dt)
    {
        IntegrateRods(s, dt);
        CoolDown(s, dt);
        s.Tick++;

        if (s.BallFrozen)
            return new StepResult(-1, false);

        // A trapped ball rides its figure instead of integrating — charge, dribble, or fire. Checked
        // *after* the freeze gate: an automatic pause (opponent drop, §3.4) freezes a live ball without
        // clearing the trap, so a trap interrupted that way must go dormant and resume cleanly rather
        // than keep charging/firing while the ball is meant to be frozen.
        if (s.TrappedRod >= 0)
            return HandleTrapped(s, dt);

        var damping = Math.Max(0, 1 - GameConstants.FrictionPerSecond * dt);
        s.BallVX *= damping;
        s.BallVY *= damping;

        // Magnus (§skill): a spinning ball curves — accelerate it perpendicular to its own velocity,
        // scaled by spin and speed, so a shot struck on the move bends in flight. The curve is baked
        // into the broadcast positions; the client just interpolates it. Spin then bleeds off, so the
        // bend is sharpest right after the strike and straightens out.
        var speed = Math.Sqrt(s.BallVX * s.BallVX + s.BallVY * s.BallVY);
        if (s.BallSpin != 0 && speed > 1e-6)
        {
            var accel = GameConstants.MagnusCoefficient * s.BallSpin * speed * dt;
            var ux = s.BallVX / speed;
            var uy = s.BallVY / speed;
            // Perpendicular to velocity (unit velocity rotated +90°: (-uy, ux)). Nearly zero net work,
            // and ClampSpeed keeps the tiny magnitude drift from the discrete step in check.
            s.BallVX += -uy * accel;
            s.BallVY += ux * accel;
        }
        s.BallSpin *= Math.Max(0, 1 - GameConstants.SpinDecayPerSecond * dt);

        var px = s.BallX;
        var py = s.BallY;
        var nx = px + s.BallVX * dt;
        var ny = py + s.BallVY * dt;

        const double r = GameConstants.BallRadius;
        const double h = GameConstants.TableHeight;
        const double w = GameConstants.TableWidth;
        const double rest = GameConstants.WallRestitution;

        // Side walls.
        if (ny < r)
        {
            ny = 2 * r - ny;
            s.BallVY = -s.BallVY * rest;
        }
        else if (ny > h - r)
        {
            ny = 2 * (h - r) - ny;
            s.BallVY = -s.BallVY * rest;
        }

        // End walls and goals — tested on the prev→new movement segment so the goal line can't
        // be tunneled (§4.6). Inside the mouth the ball passes the bounce plane; a goal counts
        // only once it has fully crossed the goal line (§2.4).
        if (nx < r)
        {
            if (!InMouth(YAtX(px, py, nx, ny, r)) && px >= r)
            {
                nx = 2 * r - nx;
                s.BallVX = -s.BallVX * rest;
            }
            else if (nx < -r)
            {
                s.BallX = nx;
                s.BallY = ny;
                return new StepResult(1, false); // in A's goal → B scores
            }
        }
        else if (nx > w - r)
        {
            if (!InMouth(YAtX(px, py, nx, ny, w - r)) && px <= w - r)
            {
                nx = 2 * (w - r) - nx;
                s.BallVX = -s.BallVX * rest;
            }
            else if (nx > w + r)
            {
                s.BallX = nx;
                s.BallY = ny;
                return new StepResult(0, false); // in B's goal → A scores
            }
        }

        var touched = HandleFigures(s, ref nx, ref ny);

        ClampSpeed(s);

        s.BallX = nx;
        s.BallY = ny;
        return new StepResult(-1, touched);
    }

    private static void IntegrateRods(SimState s, double dt)
    {
        for (var i = 0; i < 8; i++)
        {
            var rod = GameConstants.Rods[i];

            // Aiming goalie (§skill-aim): while the keeper holds its trapped ball and charges (SPACE
            // held), the rod freezes and ↑/↓ swing the shot's aim instead of sliding it — so the cannon
            // can be placed precisely. The client mirror (predictOwnRods) applies the identical rule, so
            // the frozen rod doesn't rubber-band against the snapshot stream.
            if (i == s.TrappedRod && rod.Role == "GK" && s.RodSpace[i])
            {
                s.AimAngle = Math.Clamp(
                    s.AimAngle + s.RodDir[i] * GameConstants.GoalieAimRate * dt,
                    -GameConstants.GoalieAimMaxAngle, GameConstants.GoalieAimMaxAngle);
                s.RodVel[i] = 0;
                continue; // rod holds position while aiming
            }

            var prev = s.RodOffset[i];
            double next;
            if (s.RodGlide[i])
            {
                // Unowned side eases to center at full speed (no ramp — it's cosmetic auto-centering).
                next = MoveToward(prev, 0.5, GameConstants.RodSpeed / rod.Travel * dt);
                s.RodVel[i] = (next - prev) * rod.Travel / dt;
            }
            else
            {
                // Accelerate the rod's speed toward the held-direction target, then integrate position.
                // RodVel doubles as the ramp state: it equals the commanded speed while the rod moves
                // freely and is zeroed at a travel limit (no coast into the wall), so reading it back
                // next tick continues the ramp. Speeding up in the held direction uses the gentle
                // RodAccel; stopping or reversing uses the snappier RodDecel — quick tap = small nudge,
                // held = full sweep, release = a near-instant stop.
                var target = s.RodDir[i] * GameConstants.RodSpeed;
                var v = s.RodVel[i];
                var speedingUp = s.RodDir[i] != 0 && (v == 0 || Math.Sign(v) == Math.Sign(target));
                var rate = speedingUp ? GameConstants.RodAccel : GameConstants.RodDecel;
                v = MoveToward(v, target, rate * dt);
                next = Math.Clamp(prev + v / rod.Travel * dt, 0, 1);
                s.RodVel[i] = next is <= 0.0 or >= 1.0 ? 0 : v;
            }
            s.RodOffset[i] = next;
        }
    }

    private static double Contact(RodDef rod) => GameConstants.BallRadius + rod.Radius;

    private static bool HandleFigures(SimState s, ref double nx, ref double ny)
    {
        // The kicker's collider stays ignored until the ball separates from it (§2.3).
        if (s.IgnoreRod >= 0)
        {
            var rod = GameConstants.Rods[s.IgnoreRod];
            var figY = rod.FigureY(s.RodOffset[s.IgnoreRod], s.IgnoreFigure);
            var sep = Contact(rod) + 1;
            if (Dist2(nx - rod.X, ny - figY) > sep * sep)
            {
                s.IgnoreRod = -1;
                s.IgnoreFigure = -1;
            }
        }

        // Deepest-overlapping figure in contact range wins (§2.3). Contact range is per-rod (the
        // goalie is fatter), so we rank by penetration depth — with a uniform radius that's just
        // "nearest", but it stays correct when radii differ.
        var bestPen = 0.0;
        int bestRod = -1, bestFig = -1;
        double bestFigY = 0, bestContact = 0, bestDist = 0;
        for (var i = 0; i < 8; i++)
        {
            var rod = GameConstants.Rods[i];
            var contact = Contact(rod);
            var dx = nx - rod.X;
            if (Math.Abs(dx) > contact)
                continue;
            for (var f = 0; f < rod.FigureCount; f++)
            {
                if (i == s.IgnoreRod && f == s.IgnoreFigure)
                    continue;
                var figY = rod.FigureY(s.RodOffset[i], f);
                var d2 = Dist2(dx, ny - figY);
                if (d2 >= contact * contact)
                    continue;
                var pen = contact - Math.Sqrt(d2);
                if (pen > bestPen)
                {
                    bestPen = pen;
                    bestRod = i;
                    bestFig = f;
                    bestFigY = figY;
                    bestContact = contact;
                    bestDist = contact - pen;
                }
            }
        }

        if (bestRod < 0)
            return false;

        var best = GameConstants.Rods[bestRod];

        // Kick key held on this rod → trap the ball on the figure instead of auto-kicking. The ball
        // sticks to the front of the man and charges until released (HandleTrapped). Fully opt-in:
        // rods with no held kick behave exactly as before. Takes precedence over the cooldown so a
        // held kick always catches (a just-kicked figure is skipped anyway via IgnoreRod above).
        if (s.RodKick[bestRod])
        {
            var trapDir = best.Side == 0 ? 1 : -1;
            s.TrappedRod = bestRod;
            s.TrappedFigure = bestFig;
            s.ChargeSeconds = 0;
            s.HoldSeconds = 0;
            s.OneTimerArmed = false; // a fresh catch is not a one-timer (§skill-onetimer)
            s.AimAngle = 0; // straight-ahead until the keeper swings it (§skill-aim)
            s.TrapY = bestFigY;
            s.BallVX = 0;
            s.BallVY = 0;
            s.BallSpin = 0; // a caught ball is dead — spin is set fresh when it's launched.
            nx = best.X + trapDir * (bestContact - 2);
            ny = bestFigY;
            return true;
        }

        if (s.KickCooldown[bestRod][bestFig] <= 0)
        {
            // Auto-kick toward the opponent's goal. Vertical deflection = contact offset plus
            // "english" from the rod's motion (§2.3); horizontal power rises with rod speed, so
            // striking on the move flies harder than a passive block (FireShot).
            var offset = Math.Clamp((ny - bestFigY) / bestContact, -1, 1);
            var powerFrac = Math.Min(1, Math.Abs(s.RodVel[bestRod]) / GameConstants.RodSpeed);
            FireShot(s, best, bestFig, powerFrac, offset);
            return true;
        }

        // Cooldown active → passive collider: reflect the relative velocity about the contact
        // normal in the (vertically moving) figure's frame, then push the ball out.
        var dist = bestDist;
        double nxn, nyn;
        if (dist < 1e-6)
        {
            nxn = best.Side == 0 ? 1 : -1;
            nyn = 0;
            dist = 0;
        }
        else
        {
            nxn = (nx - best.X) / dist;
            nyn = (ny - bestFigY) / dist;
        }

        var relVX = s.BallVX;
        var relVY = s.BallVY - s.RodVel[bestRod];
        var dot = relVX * nxn + relVY * nyn;
        if (dot < 0)
        {
            relVX -= (1 + GameConstants.WallRestitution) * dot * nxn;
            relVY -= (1 + GameConstants.WallRestitution) * dot * nyn;
        }
        s.BallVX = relVX;
        s.BallVY = relVY + s.RodVel[bestRod];
        nx = best.X + nxn * (bestContact + 0.5);
        ny = bestFigY + nyn * (bestContact + 0.5);
        return true;
    }

    /// <summary>Launches the ball from figure <paramref name="fig"/> on <paramref name="rod"/> toward
    /// the opponent goal. <paramref name="powerFrac"/> 0..1 says how far up the power ramp this shot is
    /// (rod speed for an auto-kick, charge for a trap-shot); <paramref name="offset"/> -1..1 is the
    /// vertical contact offset. <paramref name="powerBonus"/> is the extra speed (as a fraction of
    /// <see cref="GameConstants.KickSpeed"/>) granted at full <paramref name="powerFrac"/> — the
    /// ordinary <see cref="GameConstants.KickPowerBonus"/> by default, the goalie's much bigger caught-shot
    /// bonus for its cannon (§skill), so a goalie shot ramps from regular strength to near-max with charge.
    /// English (rod momentum) is read from the rod's live velocity, and the kicker's collider is ignored
    /// until the ball separates (§2.3).</summary>
    private static void FireShot(SimState s, RodDef rod, int fig, double powerFrac, double offset,
        double powerBonus = GameConstants.KickPowerBonus, double? aimAngle = null)
    {
        var dir = rod.Side == 0 ? 1 : -1;
        var speed = GameConstants.KickSpeed * (1 + powerBonus * powerFrac);
        if (aimAngle is { } aim)
        {
            // Aimed goalie cannon (§skill-aim): fly dead straight along the chosen angle. Precision is
            // the whole point, so no english/curve here — the flick-aimed english below stays the
            // identity of the auto-kick and the outfield trap-shots. cos is always positive over the
            // sub-90° aim cone, so the shot still leaves toward the opponent goal.
            s.BallVX = speed * Math.Cos(aim) * dir;
            s.BallVY = speed * Math.Sin(aim);
            s.BallSpin = 0;
        }
        else
        {
            s.BallVX = speed * dir;
            s.BallVY = offset * GameConstants.MaxDeflection
                       + GameConstants.RodMomentumTransfer * s.RodVel[rod.Index];
            // Curve (§skill): english from the rod's motion at contact plus the off-center clip. A still
            // block imparts none; a committed swipe hooks the shot mid-flight (see the Magnus step). Signed
            // by the shot direction: spin is angular, so its sense must flip with the strike direction
            // (table mirror symmetry) or the same rod slide would hook one team's shots and straighten the
            // other's — unlike the linear rod-momentum term above, which is direction-independent.
            s.BallSpin = dir * Math.Clamp(
                GameConstants.KickSpinFromRod * s.RodVel[rod.Index] / GameConstants.RodSpeed
                + GameConstants.KickSpinFromOffset * offset,
                -GameConstants.MaxSpin, GameConstants.MaxSpin);
        }
        ClampSpeed(s);
        s.KickCooldown[rod.Index][fig] = GameConstants.KickCooldownSeconds;
        s.IgnoreRod = rod.Index;
        s.IgnoreFigure = fig;
        s.LastKickRod = rod.Index;
        s.LastKickFigure = fig;
        s.LastKickTick = s.Tick;
    }

    /// <summary>A trapped ball: pinned to the front of its figure, riding the rod as it slides
    /// (dribble) and charging while the kick key stays held. Releasing the key — or hitting the safety
    /// timeout — fires it with power from the charge. Outfield rods aim by the rod's motion at that
    /// instant (a pull/push flick shot with english); the goalie instead freezes while charging and
    /// aims an explicit angle with ↑/↓ (§skill-aim), firing dead straight along it. A caught shot
    /// carries the rod's kick-power multiplier — the goalie's cannon (§skill).</summary>
    private static StepResult HandleTrapped(SimState s, double dt)
    {
        var rod = GameConstants.Rods[s.TrappedRod];
        var isGoalie = rod.Role == "GK";

        // SPACE while trapped, resolved by context (§skill). For the goalie SPACE is charge-and-launch
        // (below): pressing it starts the power charge and *releasing* it fires — the room turns the
        // release edge into this PassRequested. For every other rod SPACE is a tap on the press edge:
        //  • sliding toward an adjacent man on this rod → lane pass — a controlled hop between your own
        //    figures (stays trapped, not an interceptable toss). The clocks reset, so each pass buys a
        //    fresh setup window on the new man.
        //  • else a rod sits behind you (toward your own goal) → back-pass: a soft toss to it that
        //    *does* leave the rod, so a teammate rod behind can trap it but an opponent rod standing in
        //    the lane can pick it off (geometry decides — DEF→GK is clear, ATK→MID crosses the enemy MID).
        //  • else you are the last line (goalie) → launch the charged shot forward now.
        if (s.PassRequested)
        {
            s.PassRequested = false;
            var slide = s.RodDir[s.TrappedRod];
            var laneTarget = s.TrappedFigure + slide;
            if (!isGoalie && slide != 0 && laneTarget >= 0 && laneTarget < rod.FigureCount)
            {
                s.TrappedFigure = laneTarget;
                s.ChargeSeconds = 0;
                s.HoldSeconds = 0;
                s.OneTimerArmed = true;  // release before the ball settles → one-timer (§skill-onetimer)
                s.LastPassTick = s.Tick; // lane-pass hop → client plays a pass sound (no swing)
            }
            else if (GameConstants.RodBehind(s.TrappedRod) >= 0)
            {
                FireBackPass(s, rod, s.TrappedFigure);
                EndTrap(s);
                return new StepResult(-1, true);
            }
            else
            {
                s.BallY = rod.FigureY(s.RodOffset[s.TrappedRod], s.TrappedFigure);
                var launchFrac = Math.Clamp(s.ChargeSeconds / GameConstants.MaxCharge(s.TrappedRod), 0, 1);
                // The goalie launches dead straight along its aimed angle (§skill-aim); the offset/english
                // path is unused for it since the rod was frozen while aiming.
                FireShot(s, rod, s.TrappedFigure, launchFrac, offset: 0,
                    powerBonus: GameConstants.CaughtPowerBonus(s.TrappedRod), aimAngle: s.AimAngle);
                EndTrap(s);
                return new StepResult(-1, true);
            }
        }

        var fig = s.TrappedFigure;
        var figY = rod.FigureY(s.RodOffset[s.TrappedRod], fig);
        var dir = rod.Side == 0 ? 1 : -1;

        // Ease toward the target figure instead of snapping: dribble tracks tightly (follow speed >
        // rod speed) but a pass visibly slides the ball one man along rather than teleporting.
        s.TrapY = MoveToward(s.TrapY, figY, GameConstants.TrapFollowSpeed * dt);
        // Hold slightly inside contact range so it reads as cradled on the foot, not floating off it.
        s.BallX = rod.X + dir * (Contact(rod) - 2);
        s.BallY = s.TrapY;

        // The hold clock always ticks (auto-fire backstop). Shot power charges only while the rod's
        // charge input is held: the catch key for an outfield rod (it powers up as you cradle it), but
        // SPACE for the goalie — so the keeper builds strength deliberately, releasing to launch (§skill).
        // For the goalie, holding SPACE also *keeps* the ball, so dropping the catch key mid-charge
        // doesn't fire early: it launches only when SPACE is released (below, via PassRequested).
        var catchHeld = s.RodKick[s.TrappedRod];
        var charging = isGoalie ? s.RodSpace[s.TrappedRod] : catchHeld;
        var held = catchHeld || charging; // whether anything is still holding the ball trapped
        s.HoldSeconds += dt;
        if (charging)
            s.ChargeSeconds += dt;

        // Fire on release of the hold (catch key, or SPACE for a charging goalie) or the safety timeout
        // (longer for the goalie). A release *during* a pass waits until the ball has reached the man, so
        // the shot always leaves from a figure — never mid-slide between two of them. The caught shot
        // gets the rod's power bonus (ordinary for outfield, the goalie's charge-scaled cannon).
        var arrived = Math.Abs(s.TrapY - figY) < 1;

        // Once a passed ball reaches the new man while still held, the player has cradled it — it's a
        // controlled dribble now, so a later release is a normal charged shot, not a one-timer.
        if (arrived && held)
            s.OneTimerArmed = false;

        if (s.HoldSeconds >= GameConstants.TrapTimeout(s.TrappedRod) || (!held && arrived))
        {
            s.BallY = figY;
            if (s.OneTimerArmed)
            {
                // One-timer (§skill-onetimer): a lane pass struck first-time — the catch released before
                // the ball settled. No charge (the pass zeroed it), so fire at full power with the bigger
                // one-timer bonus. Pinned at figure center → aim is all in the rod's slide (english),
                // same offset-0 path as a normal outfield trap-shot; the goalie never reaches here.
                FireShot(s, rod, fig, powerFrac: 1, offset: 0, powerBonus: GameConstants.OneTimerPowerBonus);
            }
            else
            {
                // Pinned at figure center, so outfield aim is all in the release slide (english) — offset 0.
                // The goalie instead fires along its aimed angle (§skill-aim), straight, at whatever it charged.
                var powerFrac = Math.Clamp(s.ChargeSeconds / GameConstants.MaxCharge(s.TrappedRod), 0, 1);
                FireShot(s, rod, fig, powerFrac, offset: 0, powerBonus: GameConstants.CaughtPowerBonus(s.TrappedRod),
                    aimAngle: isGoalie ? s.AimAngle : null);
            }
            EndTrap(s);
            return new StepResult(-1, true);
        }

        s.BallVX = 0;
        s.BallVY = 0;
        return new StepResult(-1, true);
    }

    /// <summary>A back-pass (§skill): the trapped ball leaves the man as a soft toss toward the
    /// passer's own goal, aimed by the rod's slide at release (english). Unlike a lane pass it is a
    /// *real* ball — a rod behind can trap it, but an opponent rod in its path can intercept — so it
    /// carries risk. No power bonus, and no kick swing (a forward swing would read wrong going
    /// backward). The passer's collider is ignored until the ball separates, so it clears the man.</summary>
    private static void FireBackPass(SimState s, RodDef rod, int fig)
    {
        var backDir = rod.Side == 0 ? -1 : 1; // toward the passer's own goal
        s.BallVX = GameConstants.BackPassSpeed * backDir;
        s.BallVY = GameConstants.RodMomentumTransfer * s.RodVel[rod.Index];
        // Signed by travel direction (backDir here) so the curve is mirror-symmetric — see FireShot.
        s.BallSpin = backDir * Math.Clamp(
            GameConstants.KickSpinFromRod * s.RodVel[rod.Index] / GameConstants.RodSpeed,
            -GameConstants.MaxSpin, GameConstants.MaxSpin);
        ClampSpeed(s);
        s.KickCooldown[rod.Index][fig] = GameConstants.KickCooldownSeconds;
        s.IgnoreRod = rod.Index;
        s.IgnoreFigure = fig;
        s.LastPassTick = s.Tick; // back-pass toss → client plays a pass sound (no forward swing)
    }

    private static void EndTrap(SimState s)
    {
        s.TrappedRod = -1;
        s.TrappedFigure = -1;
        s.ChargeSeconds = 0;
        s.HoldSeconds = 0;
        s.AimAngle = 0;
        s.PassRequested = false;
        s.OneTimerArmed = false;
    }

    private static void ClampSpeed(SimState s)
    {
        var speed = Math.Sqrt(s.BallVX * s.BallVX + s.BallVY * s.BallVY);
        if (speed > GameConstants.MaxBallSpeed)
        {
            var k = GameConstants.MaxBallSpeed / speed;
            s.BallVX *= k;
            s.BallVY *= k;
        }
    }

    private static void CoolDown(SimState s, double dt)
    {
        foreach (var rod in s.KickCooldown)
            for (var f = 0; f < rod.Length; f++)
                if (rod[f] > 0)
                    rod[f] -= dt;
    }

    private static bool InMouth(double y) => y > GameConstants.GoalMouthTop && y < GameConstants.GoalMouthBottom;

    /// <summary>y of the movement segment where it crosses the vertical plane x = <paramref name="planeX"/>.</summary>
    private static double YAtX(double px, double py, double nx, double ny, double planeX) =>
        Math.Abs(nx - px) < 1e-9 ? py : py + (ny - py) * (planeX - px) / (nx - px);

    private static double MoveToward(double value, double target, double maxStep) =>
        Math.Abs(target - value) <= maxStep ? target : value + Math.Sign(target - value) * maxStep;

    private static double Dist2(double dx, double dy) => dx * dx + dy * dy;
}
