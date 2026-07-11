using System.Text.Json.Serialization;

namespace Fotbalek.Web.Game.Core;

/// <summary>Wire values of <see cref="RoomStateDto.State"/> and <see cref="SnapshotDto.State"/>.</summary>
public enum GamePhase
{
    Waiting = 0,
    Playing = 1,
    GameOver = 2,
}

/// <summary>
/// Moving game state, broadcast 30×/s to the room's SignalR group (§4.3). Property names are
/// single letters to keep the payload ~100 bytes; everything else derives client-side from
/// the static <see cref="GameConfigDto"/>.
/// </summary>
public sealed record SnapshotDto(
    [property: JsonPropertyName("t")] long Tick,
    [property: JsonPropertyName("b")] double[] Ball,
    [property: JsonPropertyName("v")] double[] BallVelocity,
    [property: JsonPropertyName("o")] double[] RodOffsets,
    [property: JsonPropertyName("s")] int[] Score,
    [property: JsonPropertyName("st")] int State,
    [property: JsonPropertyName("r")] int ResetCounter,
    [property: JsonPropertyName("k")] int[] Kick,
    [property: JsonPropertyName("kt")] long KickTick,
    [property: JsonPropertyName("mt")] int MatchSeconds,
    [property: JsonPropertyName("tr")] int TrapRod,
    [property: JsonPropertyName("tf")] int TrapFigure,
    [property: JsonPropertyName("ch")] double HoldRemaining,
    [property: JsonPropertyName("pw")] double ShotPower,
    [property: JsonPropertyName("sp")] double BallSpin,
    [property: JsonPropertyName("pt")] long LastPassTick,
    [property: JsonPropertyName("am")] double AimAngle);

/// <summary>One seat's occupancy. A seat is <see cref="Occupied"/> by a human (<see cref="UserId"/>
/// set) or a computer (<see cref="IsBot"/>); <see cref="BotLevel"/> is the <see cref="BotDifficulty"/>
/// wire value, meaningful only when <see cref="IsBot"/>.</summary>
public sealed record SeatDto(
    int Seat, int? UserId, string? Name, int? AvatarId, bool Connected, bool IsBot = false, int BotLevel = 0)
{
    public bool Occupied => UserId != null || IsBot;
}

public sealed record ViewerDto(int UserId, string Name, int AvatarId);

public sealed record WinnerDto(string Name, int AvatarId);

/// <summary>
/// Full lobby state, broadcast as one DTO on any lobby change (§4.3) — full-state-replace keeps
/// clients idempotent. <c>Closed</c> is set on the final broadcast when the room is destroyed.
/// <c>State</c>: 0 = waiting, 1 = playing, 2 = game over.
/// </summary>
public sealed record RoomStateDto(
    Guid RoomId,
    int ScoreA,
    int ScoreB,
    int State,
    bool Closed,
    IReadOnlyList<SeatDto> Seats,
    IReadOnlyList<ViewerDto> Viewers,
    IReadOnlyList<WinnerDto> Winners,
    GameOptionsDto Options);

/// <summary>Per-room, player-adjustable rules (§3.6).</summary>
public sealed record GameOptionsDto(bool DisallowQuickGoals, bool DisallowFirstTouchGoals);

public sealed record RodConfigDto(
    double X, int Side, int Figures, double Spacing, double Travel, double YBase, double Radius);

/// <summary>Static table geometry / physics constants the renderer and own-rod predictor need,
/// returned by <c>JoinRoom</c> so they live in exactly one place — the server (§4.3).</summary>
public sealed record GameConfigDto(
    double Width,
    double Height,
    double GoalMouth,
    double BallRadius,
    double FigureRadius,
    double RodSpeed,
    double RodAccel,
    double RodDecel,
    double DashSpeed,
    double DashCooldownSeconds,
    int TickRate,
    IReadOnlyList<RodConfigDto> Rods)
{
    public static GameConfigDto Create() => new(
        GameConstants.TableWidth,
        GameConstants.TableHeight,
        GameConstants.GoalMouthHeight,
        GameConstants.BallRadius,
        GameConstants.FigureRadius,
        GameConstants.RodSpeed,
        GameConstants.RodAccel,
        GameConstants.RodDecel,
        GameConstants.DashSpeed,
        GameConstants.DashCooldownSeconds,
        GameConstants.TickRate,
        GameConstants.Rods
            .Select(r => new RodConfigDto(r.X, r.Side, r.FigureCount, r.FigureSpacing, r.Travel, r.YBase, r.Radius))
            .ToList());
}

/// <summary>Everything a late joiner or reconnecting client needs to initialize.</summary>
public sealed record JoinRoomResult(GameConfigDto Config, RoomStateDto State, SnapshotDto Snapshot);

/// <summary>Distribution of one client-measured metric over a ~10 s window (§12). Percentiles are
/// computed client-side (Azure Monitor can't derive them from histograms). Diagnostic only — never
/// trusted for game logic.</summary>
public sealed record StatSummaryDto(int Count, double Min, double Mean, double P50, double P95, double Max);

/// <summary>A client's windowed latency report, sent via <c>ReportStats</c> while seated and playing
/// (§12). <see cref="Rtt"/> = hub round-trip time; <see cref="Gap"/> = snapshot inter-arrival time;
/// <see cref="Frame"/> = render frame interval (device/tab jank); <see cref="ExtrapFrames"/> of
/// <see cref="SampledFrames"/> = frames the client had no future snapshot and had to extrapolate the
/// ball (interpolation-buffer health — a rising fraction means the buffer is too tight for the jitter).</summary>
public sealed record ClientStatsDto(
    StatSummaryDto Rtt,
    StatSummaryDto Gap,
    StatSummaryDto Frame,
    int ExtrapFrames,
    int SampledFrames);
