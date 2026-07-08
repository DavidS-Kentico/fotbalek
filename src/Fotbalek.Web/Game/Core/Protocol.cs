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
/// Moving game state, broadcast 20×/s to the room's SignalR group (§4.3). Property names are
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
    [property: JsonPropertyName("ch")] double HoldRemaining);

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

/// <summary>Per-room, player-adjustable rules. Extensible — one flag today.</summary>
public sealed record GameOptionsDto(bool DisallowQuickGoals);

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
        GameConstants.TickRate,
        GameConstants.Rods
            .Select(r => new RodConfigDto(r.X, r.Side, r.FigureCount, r.FigureSpacing, r.Travel, r.YBase, r.Radius))
            .ToList());
}

/// <summary>Everything a late joiner or reconnecting client needs to initialize.</summary>
public sealed record JoinRoomResult(GameConfigDto Config, RoomStateDto State, SnapshotDto Snapshot);
