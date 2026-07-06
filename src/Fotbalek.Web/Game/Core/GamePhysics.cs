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

        var speed = Math.Sqrt(s.BallVX * s.BallVX + s.BallVY * s.BallVY);
        if (speed > GameConstants.MaxBallSpeed)
        {
            var k = GameConstants.MaxBallSpeed / speed;
            s.BallVX *= k;
            s.BallVY *= k;
        }

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

    private static bool HandleFigures(SimState s, ref double nx, ref double ny)
    {
        const double contact = GameConstants.BallRadius + GameConstants.FigureRadius;

        // The kicker's collider stays ignored until the ball separates from it (§2.3).
        if (s.IgnoreRod >= 0)
        {
            var rod = GameConstants.Rods[s.IgnoreRod];
            var figY = rod.FigureY(s.RodOffset[s.IgnoreRod], s.IgnoreFigure);
            if (Dist2(nx - rod.X, ny - figY) > (contact + 1) * (contact + 1))
            {
                s.IgnoreRod = -1;
                s.IgnoreFigure = -1;
            }
        }

        // Nearest figure in contact range wins (§2.3).
        var bestD2 = contact * contact;
        int bestRod = -1, bestFig = -1;
        double bestFigY = 0;
        for (var i = 0; i < 8; i++)
        {
            var rod = GameConstants.Rods[i];
            var dx = nx - rod.X;
            if (Math.Abs(dx) > contact)
                continue;
            for (var f = 0; f < rod.FigureCount; f++)
            {
                if (i == s.IgnoreRod && f == s.IgnoreFigure)
                    continue;
                var figY = rod.FigureY(s.RodOffset[i], f);
                var d2 = Dist2(dx, ny - figY);
                if (d2 < bestD2)
                {
                    bestD2 = d2;
                    bestRod = i;
                    bestFig = f;
                    bestFigY = figY;
                }
            }
        }

        if (bestRod < 0)
            return false;

        var best = GameConstants.Rods[bestRod];
        if (s.KickCooldown[bestRod][bestFig] <= 0)
        {
            // Auto-kick toward the opponent's goal; vertical deflection = contact offset
            // plus a fraction of the rod's momentum ("english", §2.3).
            var dir = best.Side == 0 ? 1 : -1;
            var offset = Math.Clamp((ny - bestFigY) / contact, -1, 1);
            s.BallVX = GameConstants.KickSpeed * dir;
            s.BallVY = offset * GameConstants.MaxDeflection
                       + GameConstants.RodMomentumTransfer * s.RodVel[bestRod];
            s.KickCooldown[bestRod][bestFig] = GameConstants.KickCooldownSeconds;
            s.IgnoreRod = bestRod;
            s.IgnoreFigure = bestFig;
            s.LastKickRod = bestRod;
            s.LastKickFigure = bestFig;
            s.LastKickTick = s.Tick;
            return true;
        }

        // Cooldown active → passive collider: reflect the relative velocity about the contact
        // normal in the (vertically moving) figure's frame, then push the ball out.
        var dist = Math.Sqrt(bestD2);
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
        nx = best.X + nxn * (contact + 0.5);
        ny = bestFigY + nyn * (contact + 0.5);
        return true;
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
