namespace Fotbalek.Web.Game.Core;

/// <summary>Computer-player skill. Wire value carried in <see cref="SeatDto.BotLevel"/>.</summary>
public enum BotDifficulty
{
    Easy = 0,
    Medium = 1,
    Hard = 2,
}

/// <summary>
/// Per-rod scratch state for one bot seat: when each rod may next re-plan (reaction latency) and
/// the rod offset it is currently steering toward. Kept on the seat so the memory lives exactly as
/// long as the bot does; reset to center when the bot is seated.
/// </summary>
public sealed class BotBrain
{
    /// <summary>Sim-time (seconds) when the rod may pick a new target — indexed by rod.</summary>
    public readonly double[] NextThink = new double[8];

    /// <summary>Last chosen rod offset 0..1 — indexed by rod.</summary>
    public readonly double[] TargetOffset = new double[8];

    public BotBrain() => Array.Fill(TargetOffset, 0.5);
}

/// <summary>
/// A deliberately simple foosball "AI": each rod tracks the ball vertically, lining up its
/// best-placed figure so the auto-kick (<see cref="GamePhysics"/>) fires on contact — the kick
/// direction is decided by the rod's team, so a bot never scores on itself. Pure like the rest of
/// <c>Game/Core/</c> (no server dependencies) — the only external input is a <see cref="Random"/>
/// for aim jitter and the <see cref="BotBrain"/> the caller owns.
/// </summary>
public static class GameBot
{
    /// <param name="ReactionSeconds">How often a rod re-plans — higher means laggier, more human.</param>
    /// <param name="Lookahead">Seconds of ball velocity added to the aim point (leading the ball).</param>
    /// <param name="AimErrorFrac">Random aim error as a fraction of table height.</param>
    /// <param name="DeadzoneFrac">Stop band as a fraction of rod travel (offset units) — bigger is sloppier.</param>
    /// <param name="EngageRange">Only chase while the ball is within this x-distance of the rod;
    /// otherwise ease back to center so the whole line doesn't clump on the ball.</param>
    private readonly record struct Tuning(
        double ReactionSeconds,
        double Lookahead,
        double AimErrorFrac,
        double DeadzoneFrac,
        double EngageRange);

    private static Tuning For(BotDifficulty d) => d switch
    {
        BotDifficulty.Easy => new(0.28, 0.00, 0.10, 0.20, 360),
        BotDifficulty.Medium => new(0.14, 0.06, 0.05, 0.10, 600),
        _ => new(0.05, 0.12, 0.02, 0.05, 3000), // Hard: fast, predictive, precise, always engaged
    };

    /// <summary>Held direction for one rod this tick: -1 up, 0 stop, +1 down (matches
    /// <see cref="SimState.RodDir"/>). Call once per rod the bot controls, every tick.</summary>
    public static int DecideRod(SimState s, int rod, BotDifficulty diff, double time, Random rng, BotBrain brain)
    {
        var tuning = For(diff);

        // Re-plan only every ReactionSeconds; between plans the rod keeps gliding toward the last
        // target, so lower difficulties respond with a visible, human-like delay.
        if (time >= brain.NextThink[rod])
        {
            brain.NextThink[rod] = time + tuning.ReactionSeconds;
            brain.TargetOffset[rod] = PlanOffset(s, GameConstants.Rods[rod], tuning, rng);
        }

        var delta = brain.TargetOffset[rod] - s.RodOffset[rod];
        if (Math.Abs(delta) <= tuning.DeadzoneFrac)
            return 0;
        return delta > 0 ? 1 : -1; // offset grows downward (larger y), so +1 = down
    }

    /// <summary>Target rod offset 0..1: the offset that lands the best-placed figure on the ball's
    /// (velocity-led, jittered) y. Far from the rod's lane it returns center.</summary>
    private static double PlanOffset(SimState s, RodDef rod, Tuning tuning, Random rng)
    {
        if (Math.Abs(s.BallX - rod.X) > tuning.EngageRange)
            return 0.5;

        var aimY = s.BallY
                   + s.BallVY * tuning.Lookahead
                   + (rng.NextDouble() * 2 - 1) * tuning.AimErrorFrac * GameConstants.TableHeight;
        aimY = Math.Clamp(aimY, 0, GameConstants.TableHeight);

        // Choose the figure whose reachable band gets closest to aimY, then the offset that puts it
        // there. Geometry (spacing, travel, goal-centered goalie) is owned by RodDef.
        var best = 0.5;
        var bestErr = double.MaxValue;
        for (var f = 0; f < rod.FigureCount; f++)
        {
            var off = rod.OffsetForFigureY(aimY, f);
            var err = Math.Abs(rod.FigureY(off, f) - aimY);
            if (err < bestErr)
            {
                bestErr = err;
                best = off;
            }
        }
        return best;
    }
}
