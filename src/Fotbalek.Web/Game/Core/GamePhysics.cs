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
            var prev = s.RodOffset[i];
            var maxStep = GameConstants.RodSpeed / rod.Travel * dt;
            var next = s.RodGlide[i]
                ? MoveToward(prev, 0.5, maxStep)
                : Math.Clamp(prev + s.RodDir[i] * maxStep, 0, 1);
            // Actual velocity (0 when clamped at the travel limit) — kicks read this.
            s.RodVel[i] = (next - prev) * rod.Travel / dt;
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
            s.TrapY = bestFigY;
            s.BallVX = 0;
            s.BallVY = 0;
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
    /// the opponent goal. <paramref name="powerFrac"/> 0..1 adds horizontal power on top of the base
    /// kick speed (rod speed for an auto-kick, charge for a trap-shot); <paramref name="offset"/>
    /// -1..1 is the vertical contact offset. English (rod momentum) is read from the rod's live
    /// velocity, and the kicker's collider is ignored until the ball separates (§2.3).</summary>
    private static void FireShot(SimState s, RodDef rod, int fig, double powerFrac, double offset)
    {
        var dir = rod.Side == 0 ? 1 : -1;
        var speed = GameConstants.KickSpeed * (1 + GameConstants.KickPowerBonus * powerFrac);
        s.BallVX = speed * dir;
        s.BallVY = offset * GameConstants.MaxDeflection
                   + GameConstants.RodMomentumTransfer * s.RodVel[rod.Index];
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
    /// timeout — fires it with power from the charge and english from the rod's motion at that instant
    /// (a pull/push shot: flick up or down before letting go to aim).</summary>
    private static StepResult HandleTrapped(SimState s, double dt)
    {
        var rod = GameConstants.Rods[s.TrappedRod];

        // Lane pass (SPACE): hand the ball to the adjacent man on this rod, in the direction the rod
        // is being slid — down (+1) → next man below, up (-1) → next man above. It stays trapped, so
        // this is a controlled hop between your own figures, not an interceptable toss. The hold
        // timer/charge reset, so each pass buys a fresh setup window on the new man.
        if (s.PassRequested)
        {
            s.PassRequested = false;
            var slide = s.RodDir[s.TrappedRod];
            var target = s.TrappedFigure + slide;
            if (slide != 0 && target >= 0 && target < rod.FigureCount)
            {
                s.TrappedFigure = target;
                s.ChargeSeconds = 0;
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

        var held = s.RodKick[s.TrappedRod];
        if (held)
            s.ChargeSeconds += dt;

        // Fire on release or the safety timeout. A release *during* a pass waits until the ball has
        // reached the man, so the shot always leaves from a figure — never from mid-slide between two
        // of them (which looked like the ball firing while still travelling across on the pass).
        var arrived = Math.Abs(s.TrapY - figY) < 1;
        if (s.ChargeSeconds >= GameConstants.TrapTimeoutSeconds || (!held && arrived))
        {
            // Pinned at figure center, so aim is all in the release slide (english) — offset 0.
            var powerFrac = Math.Clamp(s.ChargeSeconds / GameConstants.MaxChargeSeconds, 0, 1);
            s.BallY = figY;
            FireShot(s, rod, fig, powerFrac, offset: 0);
            s.TrappedRod = -1;
            s.TrappedFigure = -1;
            s.ChargeSeconds = 0;
            s.PassRequested = false;
            return new StepResult(-1, true);
        }

        s.BallVX = 0;
        s.BallVY = 0;
        return new StepResult(-1, true);
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
