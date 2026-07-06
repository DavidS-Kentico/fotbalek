using System.Diagnostics;
using Fotbalek.Web.Game.Core;
using Microsoft.AspNetCore.SignalR;

namespace Fotbalek.Web.Game;

/// <summary>
/// One live game: seats, viewers, score and simulation, owning its 60 Hz tick loop (§4.2).
/// All mutations — hub input intake and the Blazor page's lobby calls alike — funnel through
/// this thread-safe API. State is in-memory only; a server restart ends the game.
/// </summary>
public sealed class GameRoom
{
    private static readonly GameConfigDto Config = GameConfigDto.Create();

    public Guid RoomId { get; } = Guid.NewGuid();
    public int TeamId { get; }
    public string GroupName => $"game:{RoomId:N}";

    private readonly object _gate = new();
    private readonly GameRoomManager _manager;
    private readonly IHubContext<GameHub> _hub;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Random _rng = new();

    private sealed class SeatState
    {
        public int? UserId;
        public string Name = "";
        public int AvatarId = 1;
        /// <summary>Sim-time deadline of the 30 s grace hold; set while the occupant has zero
        /// live hub connections — however it got there (§3.5).</summary>
        public double? GraceUntil;
    }

    private sealed class UserSession
    {
        public string Name = "";
        public int AvatarId = 1;
        public readonly HashSet<string> Connections = [];
    }

    private readonly SeatState[] _seats =
        [new SeatState(), new SeatState(), new SeatState(), new SeatState()];

    /// <summary>Users with at least one live hub connection in the room, keyed by user id.
    /// Removed when the last connection goes — seat display info lives on the seat.</summary>
    private readonly Dictionary<int, UserSession> _users = [];
    private readonly Dictionary<string, int> _userByConnection = [];

    /// <summary>Held-key state per user: [left, right] hand, each -1/0/+1. Kept per user (not
    /// per seat) so it carries over on a seat swap; cleared when the seat is vacated (§4.2).</summary>
    private readonly Dictionary<int, int[]> _heldInput = [];

    private enum Pending { None, KickRandom, KickTowardA, KickTowardB, Resume }

    private readonly SimState _sim = new();
    private GamePhase _phase = GamePhase.Waiting;
    private int _scoreA;
    private int _scoreB;
    private List<WinnerDto> _winners = [];

    private double _time;
    private double _pauseUntil;
    private Pending _pending = Pending.KickRandom;
    private double _resumeVX;
    private double _resumeVY;
    private double _slowSince = -1;
    private double _lastTouch;
    private double? _emptySince = 0; // the room is born empty
    private bool _lobbyDirty;
    private bool _destroyRequested;
    private bool _closed;
    private long _lastSnapshotTick;

    internal GameRoom(int teamId, GameRoomManager manager, IHubContext<GameHub> hub, ILogger logger)
    {
        TeamId = teamId;
        _manager = manager;
        _hub = hub;
        _logger = logger;
    }

    internal void Start() => _ = Task.Run(RunLoopAsync);

    // ---- Connection lifecycle (hub adapter) -------------------------------------------------

    /// <summary>Registers a live hub connection and returns the full init payload.
    /// Null when the room is already closed.</summary>
    public JoinRoomResult? Connect(int userId, string connectionId, string name, int avatarId)
    {
        lock (_gate)
        {
            if (_closed)
                return null;

            if (!_users.TryGetValue(userId, out var session))
                _users[userId] = session = new UserSession();
            session.Name = name;
            session.AvatarId = avatarId;
            session.Connections.Add(connectionId);
            _userByConnection[connectionId] = userId;

            var seat = FindSeatLocked(userId);
            if (seat != null)
            {
                seat.GraceUntil = null;
                seat.Name = name;
                seat.AvatarId = avatarId;
            }

            _lobbyDirty = true;
            return new JoinRoomResult(Config, BuildStateLocked(), BuildSnapshotLocked());
        }
    }

    /// <summary>Removes a connection. A graceful close (page navigation, connection.stop())
    /// frees the user's seat immediately; an abnormal drop leaves it grace-held (§3.5). The
    /// seat reacts only when the user's last connection in the room goes.</summary>
    public void Disconnect(string connectionId, bool graceful)
    {
        lock (_gate)
        {
            if (!_userByConnection.Remove(connectionId, out var userId))
                return;
            if (!_users.TryGetValue(userId, out var session))
                return;

            session.Connections.Remove(connectionId);
            if (session.Connections.Count > 0)
                return;

            _users.Remove(userId);
            if (graceful && FindSeatLocked(userId) is { } seat)
                VacateLocked(seat);
            // Abnormal: the seat (if any) now has zero connections — the tick loop starts the
            // grace timer; rods freeze because the seat no longer counts as connected.
            _lobbyDirty = true;
        }
    }

    // ---- Lobby API (called from the Blazor circuit and the hub alike) ------------------------

    /// <summary>Sits the user in the given seat. Free seats only; a seated user moves (seat
    /// swap, held input carries over). Display info is resolved by the caller (§3.7).</summary>
    public bool TakeSeat(int userId, int seatIndex, string name, int avatarId)
    {
        if (seatIndex is < 0 or >= SeatMap.SeatCount)
            return false;
        lock (_gate)
        {
            if (_closed)
                return false;
            var target = _seats[seatIndex];
            if (target.UserId == userId)
                return true;
            if (target.UserId != null)
                return false;

            if (FindSeatLocked(userId) is { } current)
            {
                current.UserId = null;
                current.GraceUntil = null;
            }

            target.UserId = userId;
            target.Name = name;
            target.AvatarId = avatarId;
            target.GraceUntil = null; // liveness re-evaluated on the next tick
            _lobbyDirty = true;
            return true;
        }
    }

    public bool TryTakeFirstFreeSeat(int userId, string name, int avatarId)
    {
        lock (_gate)
        {
            if (_closed)
                return false;
            if (FindSeatLocked(userId) != null)
                return true;
            foreach (var seat in _seats)
            {
                if (seat.UserId != null)
                    continue;
                seat.UserId = userId;
                seat.Name = name;
                seat.AvatarId = avatarId;
                seat.GraceUntil = null;
                _lobbyDirty = true;
                return true;
            }
            return false;
        }
    }

    public void LeaveSeat(int userId)
    {
        lock (_gate)
        {
            if (FindSeatLocked(userId) is { } seat)
            {
                VacateLocked(seat);
                _lobbyDirty = true;
            }
        }
    }

    public void ResetScore(int userId)
    {
        lock (_gate)
        {
            if (_closed || FindSeatLocked(userId) == null)
                return;
            _scoreA = 0;
            _scoreB = 0;
            if (_phase == GamePhase.GameOver)
                RestartLocked();
            _lobbyDirty = true;
        }
    }

    /// <summary>Re-centers the ball with a small random velocity (for freezes); no-op unless
    /// playing (§3.6).</summary>
    public void ResetBall(int userId)
    {
        lock (_gate)
        {
            if (_closed || FindSeatLocked(userId) == null)
                return;
            if (_phase == GamePhase.Playing && !_sim.BallFrozen)
                ResetBallLocked();
        }
    }

    public void Rematch(int userId)
    {
        lock (_gate)
        {
            if (_closed || _phase != GamePhase.GameOver || FindSeatLocked(userId) == null)
                return;
            _scoreA = 0;
            _scoreB = 0;
            RestartLocked();
            _lobbyDirty = true;
        }
    }

    /// <summary>Destroys the room for everyone. Any seated player may end the game (§3.6).</summary>
    public bool EndGame(int userId)
    {
        lock (_gate)
        {
            if (_closed || FindSeatLocked(userId) == null)
                return false;
        }
        _manager.Destroy(RoomId);
        return true;
    }

    /// <summary>Held-key state change for one hand; ignored from non-seated users (§4.2).
    /// The server maps hand → rod(s) from the sender's seat each tick.</summary>
    public void SetHandInput(int userId, int hand, int dir)
    {
        if (hand is not (SeatMap.LeftHand or SeatMap.RightHand))
            return;
        lock (_gate)
        {
            if (_closed || FindSeatLocked(userId) == null)
                return;
            if (!_heldInput.TryGetValue(userId, out var hands))
                _heldInput[userId] = hands = new int[2];
            hands[hand] = Math.Clamp(dir, -1, 1);
        }
    }

    public RoomStateDto GetState()
    {
        lock (_gate)
        {
            return BuildStateLocked();
        }
    }

    /// <summary>Final broadcast (closed flag set) and loop shutdown. Called by the manager.</summary>
    internal async Task ShutdownAsync()
    {
        RoomStateDto final;
        lock (_gate)
        {
            if (_closed)
                return;
            _closed = true;
            final = BuildStateLocked();
        }
        _cts.Cancel();
        try
        {
            await _hub.Clients.Group(GroupName).SendAsync("roomState", final);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Final roomState broadcast failed for room {RoomId}", RoomId);
        }
        // The CTS is deliberately not disposed — the loop may still be observing the token.
    }

    // ---- Tick loop ---------------------------------------------------------------------------

    private async Task RunLoopAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(GameConstants.FixedDt));
            var sw = Stopwatch.StartNew();
            double last = 0;
            double acc = 0;
            while (await timer.WaitForNextTickAsync(_cts.Token))
            {
                // PeriodicTimer ticks aren't exact 16.7 ms — measure elapsed time and run a
                // fixed-step accumulator (§4.2), capped so a scheduler stall can't spiral.
                var now = sw.Elapsed.TotalSeconds;
                acc = Math.Min(acc + (now - last), 0.25);
                last = now;

                SnapshotDto? snapshot = null;
                var lobbyDirty = false;
                bool destroy;
                lock (_gate)
                {
                    while (acc >= GameConstants.FixedDt)
                    {
                        TickLocked(GameConstants.FixedDt);
                        acc -= GameConstants.FixedDt;
                    }
                    if (_sim.Tick - _lastSnapshotTick >= GameConstants.TicksPerSnapshot)
                    {
                        _lastSnapshotTick = _sim.Tick;
                        snapshot = BuildSnapshotLocked();
                    }
                    if (_lobbyDirty)
                    {
                        lobbyDirty = true;
                        _lobbyDirty = false;
                    }
                    destroy = _destroyRequested;
                }

                if (snapshot != null)
                    await _hub.Clients.Group(GroupName).SendAsync("snapshot", snapshot, _cts.Token);
                if (lobbyDirty)
                    await BroadcastStateAsync();
                if (destroy)
                {
                    _manager.Destroy(RoomId);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Room shut down.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Game loop crashed for room {RoomId}; destroying room", RoomId);
            _manager.Destroy(RoomId);
        }
    }

    private void TickLocked(double dt)
    {
        _time += dt;

        UpdateSeatLivenessLocked();

        var sideAOccupied = SideOccupiedLocked(0);
        var sideBOccupied = SideOccupiedLocked(1);
        var bothReady = SideReadyLocked(0) && SideReadyLocked(1);

        switch (_phase)
        {
            case GamePhase.Waiting:
                if (bothReady)
                {
                    // Short pause before the ball goes live — kickoff or reconnect resume (§3.4).
                    _phase = GamePhase.Playing;
                    _pauseUntil = _time + GameConstants.KickoffPauseSeconds;
                    _lobbyDirty = true;
                }
                else if (!sideAOccupied || !sideBOccupied)
                {
                    // A grace-held wait degrades to "waiting for opponents" once the seat frees:
                    // the ball re-parks at center and any stored velocity is discarded (§3.4).
                    ParkBallLocked(Pending.KickRandom);
                }
                break;

            case GamePhase.Playing:
                if (!bothReady)
                {
                    _phase = GamePhase.Waiting;
                    _lobbyDirty = true;
                    if (!sideAOccupied || !sideBOccupied)
                    {
                        ParkBallLocked(Pending.KickRandom);
                    }
                    else if (!_sim.BallFrozen)
                    {
                        // Automatic pause: freeze in place, store velocity, resume on reconnect.
                        _pending = Pending.Resume;
                        _resumeVX = _sim.BallVX;
                        _resumeVY = _sim.BallVY;
                        _sim.BallVX = 0;
                        _sim.BallVY = 0;
                        _sim.BallFrozen = true;
                    }
                    // Frozen mid-kickoff-pause: keep the pending kick for the resume.
                }
                else if (_sim.BallFrozen && _time >= _pauseUntil)
                {
                    ApplyPendingKickLocked();
                }
                break;

            case GamePhase.GameOver:
                break; // ball intentionally frozen; waits for Rematch / Reset score / End game.
        }

        ApplyRodInputLocked();

        var result = GamePhysics.Step(_sim, dt);
        if (result.Touched)
            _lastTouch = _time;
        if (result.GoalBySide >= 0)
            HandleGoalLocked(result.GoalBySide);

        if (_phase == GamePhase.Playing && !_sim.BallFrozen)
        {
            // Anti-stall (§2.5): slow for ~5 s or untouched for ~15 s → re-center.
            if (_sim.BallSpeed < GameConstants.StallSpeedThreshold)
            {
                if (_slowSince < 0)
                    _slowSince = _time;
                else if (_time - _slowSince >= GameConstants.StallSpeedSeconds)
                    ResetBallLocked();
            }
            else
            {
                _slowSince = -1;
            }
            if (_time - _lastTouch >= GameConstants.StallUntouchedSeconds)
                ResetBallLocked();
        }

        // Idle cleanup: no seated players and no viewers for ~2 minutes (§3.5).
        if (!_seats.Any(s => s.UserId != null) && _users.Count == 0)
        {
            _emptySince ??= _time;
            if (_time - _emptySince >= GameConstants.EmptyRoomTimeoutSeconds)
                _destroyRequested = true;
        }
        else
        {
            _emptySince = null;
        }
    }

    private void UpdateSeatLivenessLocked()
    {
        foreach (var seat in _seats)
        {
            if (seat.UserId is not int userId)
                continue;
            if (IsConnectedLocked(userId))
            {
                seat.GraceUntil = null;
            }
            else if (seat.GraceUntil == null)
            {
                // Starts whenever the connection count hits zero — also covers seats taken
                // before the JS client connects (§3.5).
                seat.GraceUntil = _time + GameConstants.SeatGraceSeconds;
            }
            else if (_time >= seat.GraceUntil)
            {
                VacateLocked(seat);
                _lobbyDirty = true;
            }
        }
    }

    /// <summary>Maps held-hand state to per-rod directions (§2.2): two seats on a side drive
    /// their own two rods each; a lone player drives all four in pairs; a disconnected
    /// (grace-held) seat's rods freeze; a fully unseated side glides back to center.</summary>
    private void ApplyRodInputLocked()
    {
        for (var side = 0; side < 2; side++)
        {
            var seats = SeatsOfSideLocked(side);
            var occupied = seats.Where(i => _seats[i].UserId != null).ToArray();

            foreach (var rod in SeatMap.SideRods(side))
            {
                _sim.RodDir[rod] = 0;
                _sim.RodGlide[rod] = occupied.Length == 0;
            }
            if (occupied.Length == 0)
                continue;

            var alone = occupied.Length == 1;
            foreach (var seatIndex in occupied)
            {
                var userId = _seats[seatIndex].UserId!.Value;
                if (!IsConnectedLocked(userId) || !_heldInput.TryGetValue(userId, out var hands))
                    continue;
                for (var hand = 0; hand < 2; hand++)
                {
                    foreach (var rod in SeatMap.RodsFor(seatIndex, hand, alone))
                        _sim.RodDir[rod] = hands[hand];
                }
            }
        }
    }

    private void HandleGoalLocked(int scoringSide)
    {
        if (scoringSide == 0)
            _scoreA++;
        else
            _scoreB++;
        _lobbyDirty = true; // header score + overlays ride room state (§5.1)

        var score = scoringSide == 0 ? _scoreA : _scoreB;
        if (score >= GameConstants.WinningScore)
        {
            _phase = GamePhase.GameOver;
            // Winners by name, captured at the moment the winning goal lands (§2.4).
            _winners = SeatsOfSideLocked(scoringSide)
                .Where(i => _seats[i].UserId != null)
                .Select(i => new WinnerDto(_seats[i].Name, _seats[i].AvatarId))
                .ToList();
            ParkBallLocked(Pending.None);
        }
        else
        {
            // ~1 s pause, then a gentle kickoff toward the team that conceded (§2.4).
            ParkBallLocked(scoringSide == 0 ? Pending.KickTowardB : Pending.KickTowardA);
            _pauseUntil = _time + GameConstants.KickoffPauseSeconds;
        }
    }

    private void ApplyPendingKickLocked()
    {
        var angle = (_rng.NextDouble() * 2 - 1) * Math.PI / 8; // ±22.5°
        switch (_pending)
        {
            case Pending.Resume:
                _sim.BallVX = _resumeVX;
                _sim.BallVY = _resumeVY;
                break;
            case Pending.KickTowardA:
            case Pending.KickTowardB:
                var dir = _pending == Pending.KickTowardA ? -1 : 1;
                _sim.BallVX = dir * GameConstants.KickoffSpeed * Math.Cos(angle);
                _sim.BallVY = GameConstants.KickoffSpeed * Math.Sin(angle);
                break;
            default:
                var randomDir = _rng.Next(2) == 0 ? -1 : 1;
                _sim.BallVX = randomDir * GameConstants.KickoffSpeed * Math.Cos(angle);
                _sim.BallVY = GameConstants.KickoffSpeed * Math.Sin(angle);
                break;
        }
        _pending = Pending.None;
        _sim.BallFrozen = false;
        _slowSince = -1;
        _lastTouch = _time;
    }

    /// <summary>Teleports the ball to center, frozen, remembering what should happen when play
    /// (re)starts. Idempotent — the reset counter only bumps on an actual move.</summary>
    private void ParkBallLocked(Pending pending)
    {
        const double cx = GameConstants.TableWidth / 2;
        const double cy = GameConstants.TableHeight / 2;
        if (_sim.BallX != cx || _sim.BallY != cy || _sim.BallVX != 0 || _sim.BallVY != 0)
        {
            _sim.BallX = cx;
            _sim.BallY = cy;
            _sim.BallVX = 0;
            _sim.BallVY = 0;
            _sim.ResetCounter++;
        }
        _sim.BallFrozen = true;
        _sim.IgnoreRod = -1;
        _sim.IgnoreFigure = -1;
        _pending = pending;
    }

    /// <summary>Immediate re-center with a small random velocity — manual and stall resets (§2.5).</summary>
    private void ResetBallLocked()
    {
        ParkBallLocked(Pending.KickRandom);
        ApplyPendingKickLocked();
    }

    /// <summary>Back to a fresh kickoff keeping seats — rematch, or reset-score from game over.</summary>
    private void RestartLocked()
    {
        _winners = [];
        _phase = GamePhase.Waiting;
        ParkBallLocked(Pending.KickRandom);
    }

    private void VacateLocked(SeatState seat)
    {
        if (seat.UserId is int userId)
            _heldInput.Remove(userId);
        seat.UserId = null;
        seat.GraceUntil = null;
    }

    private SeatState? FindSeatLocked(int userId) =>
        _seats.FirstOrDefault(s => s.UserId == userId);

    private bool IsConnectedLocked(int userId) =>
        _users.TryGetValue(userId, out var session) && session.Connections.Count > 0;

    private static int[] SeatsOfSideLocked(int side) =>
        side == 0 ? [SeatMap.ADefense, SeatMap.AAttack] : [SeatMap.BDefense, SeatMap.BAttack];

    private bool SideOccupiedLocked(int side) =>
        SeatsOfSideLocked(side).Any(i => _seats[i].UserId != null);

    /// <summary>Ball in play needs at least one *connected* seated player on the side — a
    /// grace-held seat keeps its owner but doesn't count (§3.4).</summary>
    private bool SideReadyLocked(int side) =>
        SeatsOfSideLocked(side).Any(i => _seats[i].UserId is int u && IsConnectedLocked(u));

    // ---- Wire payloads -----------------------------------------------------------------------

    private SnapshotDto BuildSnapshotLocked() => new(
        _sim.Tick,
        [Math.Round(_sim.BallX, 1), Math.Round(_sim.BallY, 1)],
        [Math.Round(_sim.BallVX, 1), Math.Round(_sim.BallVY, 1)],
        _sim.RodOffset.Select(o => Math.Round(o, 4)).ToArray(),
        [_scoreA, _scoreB],
        (int)_phase,
        _sim.ResetCounter,
        [_sim.LastKickRod, _sim.LastKickFigure],
        _sim.LastKickTick);

    private RoomStateDto BuildStateLocked() => new(
        RoomId,
        _scoreA,
        _scoreB,
        (int)_phase,
        _closed,
        _seats.Select((s, i) => new SeatDto(
            i,
            s.UserId,
            s.UserId != null ? s.Name : null,
            s.UserId != null ? s.AvatarId : null,
            s.UserId is int u && IsConnectedLocked(u))).ToList(),
        _users
            .Where(kv => kv.Value.Connections.Count > 0 && FindSeatLocked(kv.Key) == null)
            .Select(kv => new ViewerDto(kv.Key, kv.Value.Name, kv.Value.AvatarId))
            .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ToList(),
        _winners);

    private async Task BroadcastStateAsync()
    {
        RoomStateDto state;
        lock (_gate)
        {
            state = BuildStateLocked();
        }
        try
        {
            await _hub.Clients.Group(GroupName).SendAsync("roomState", state, _cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        _manager.NotifyChanged();
    }
}
