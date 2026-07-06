namespace Fotbalek.Web.Game.Core;

/// <summary>
/// One rod on the table. <c>Side</c>: 0 = team A (attacks +x, goal on the left),
/// 1 = team B (attacks -x, goal on the right).
/// </summary>
public sealed record RodDef(int Index, double X, int Side, int FigureCount, string Role)
{
    /// <summary>Distance between adjacent figure centers — tableHeight / figureCount, the unique
    /// spacing where every rod sweeps the full table height with zero overlap and zero dead lanes.</summary>
    public double FigureSpacing => GameConstants.TableHeight / FigureCount;

    /// <summary>Vertical slide range of the rod (= tableHeight / figureCount with the spacing above).</summary>
    public double Travel => GameConstants.TableHeight - (FigureCount - 1) * FigureSpacing;

    /// <summary>Center y of figure <paramref name="i"/> (0 = topmost) at rod offset 0..1.</summary>
    public double FigureY(double offset, int i) => offset * Travel + i * FigureSpacing;
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
    public const double KickSpeed = 900;
    public const double MaxDeflection = 500;
    public const double RodMomentumTransfer = 0.35;
    public const double KickCooldownSeconds = 0.2;
    public const double MaxBallSpeed = 1400;
    public const double FrictionPerSecond = 0.3;
    public const double WallRestitution = 0.85;
    public const double KickoffSpeed = 250;

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
    /// The single goalie sweeps the full table height (travel = H/n with n = 1).</summary>
    public static readonly IReadOnlyList<RodDef> Rods =
    [
        new(0, 75, 0, 1, "GK"),
        new(1, 225, 0, 2, "DEF"),
        new(2, 375, 1, 3, "ATK"),
        new(3, 525, 0, 5, "MID"),
        new(4, 675, 1, 5, "MID"),
        new(5, 825, 0, 3, "ATK"),
        new(6, 975, 1, 2, "DEF"),
        new(7, 1125, 1, 1, "GK"),
    ];
}
