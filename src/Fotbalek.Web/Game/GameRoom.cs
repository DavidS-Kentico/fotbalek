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

        /// <summary>Non-null when a computer holds the seat (never together with <see cref="UserId"/>).
        /// A bot has no hub connection: it counts as always-connected and always-ready, and its rods
        /// are driven by <see cref="GameBot"/> each tick instead of held-key input.</summary>
        public BotDifficulty? Bot;
        public BotBrain? Brain;

        public bool Occupied => UserId != null || Bot != null;
    }

    /// <summary>Display avatar for a bot seat — a robot emoji mapped in <c>Avatar.razor</c>,
    /// outside the 1..20 range the human avatar picker offers.</summary>
    private const int BotAvatarId = 21;

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

    /// <summary>Catch state per user: true while the user holds a catch key (either Shift). One flag,
    /// not per-hand — catching has no side, so a held catch arms *all* the user's rods and the ball
    /// traps on whichever of their figures it reaches. Same seat-swap carry-over / vacate cleanup as
    /// <see cref="_heldInput"/>.</summary>
    private readonly Dictionary<int, bool> _catchHeld = [];

    /// <summary>Held-SPACE state per user: true while the user holds SPACE. Feeds <see cref="SimState.RodSpace"/>
    /// each tick so the goalie charges while it's down. Same seat-swap carry-over / vacate cleanup as
    /// <see cref="_heldInput"/>.</summary>
    private readonly Dictionary<int, bool> _spaceHeld = [];

    /// <summary>Per-hand lift state per user (§skill-lift): [left, right], each true while that hand's
    /// lift key (A/D, or ←/→) is down. Per-hand (unlike catch) so a player can raise one rod while the
    /// other keeps playing — e.g. lift the ATK to open a lane the MID shoots through. Feeds
    /// <see cref="SimState.RodLifted"/> each tick. Same seat-swap carry-over / vacate cleanup as
    /// <see cref="_heldInput"/>.</summary>
    private readonly Dictionary<int, bool[]> _liftHeld = [];

    /// <summary>Sim time each user may dash again (§skill-dash) — a shared per-user cooldown covering
    /// both their rods, so one SPACE tap bursts everything they're moving and then rests. Same
    /// seat-swap carry-over (it's keyed by user) / vacate cleanup as <see cref="_heldInput"/>.</summary>
    private readonly Dictionary<int, double> _dashReadyAt = [];

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
    private double _resumeSpin;
    private double _slowSince = -1;
    private double _lastTouch;

    /// <summary>Seconds of play in the current match — accumulates while playing, resets on a fresh
    /// match (rematch / reset score / new game). Shown as the match clock (§5.1).</summary>
    private double _matchElapsed;

    /// <summary>Sim time the current round's ball went live; goals within
    /// <see cref="GameConstants.QuickGoalGraceSeconds"/> of it don't count when
    /// <see cref="GameOptionsDto.DisallowQuickGoals"/> is on. Negative-infinity = grace disabled for
    /// this round (a reconnect resume, which is not a fresh kickoff).</summary>
    private double _roundLiveSince = double.NegativeInfinity;

    /// <summary>Figure contacts (rising edges) since the current round went live, and whether the ball
    /// was ever trapped/controlled this round. Drive the first-touch rule: a goal off ≤
    /// <see cref="GameConstants.FirstTouchMaxContacts"/> uncontrolled contacts is a "let" when
    /// <see cref="GameOptionsDto.DisallowFirstTouchGoals"/> is on. Reset on each fresh kickoff.</summary>
    private int _roundTouchCount;
    private bool _roundControlled;
    private bool _touchedLastStep;

    private bool _disallowQuickGoals = true;
    private bool _disallowFirstTouchGoals;
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
            // A bot-held seat can be taken over directly — the human evicts the computer (§3.6).
            target.Bot = null;
            target.Brain = null;

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
                if (seat.Occupied)
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

    /// <summary>Seats a computer player in a free seat. Any team member in the room may do this
    /// (mirrors take-seat, §3.6); occupied seats — human or bot — are rejected.</summary>
    public bool AddBot(int seatIndex, BotDifficulty difficulty)
    {
        if (seatIndex is < 0 or >= SeatMap.SeatCount)
            return false;
        lock (_gate)
        {
            if (_closed)
                return false;
            var target = _seats[seatIndex];
            if (target.Occupied)
                return false;
            target.Bot = difficulty;
            target.Brain = new BotBrain();
            target.GraceUntil = null;
            _lobbyDirty = true;
            return true;
        }
    }

    /// <summary>Removes a computer from its seat, freeing it. No-op on a human or empty seat.</summary>
    public void RemoveBot(int seatIndex)
    {
        if (seatIndex is < 0 or >= SeatMap.SeatCount)
            return;
        lock (_gate)
        {
            var seat = _seats[seatIndex];
            if (seat.Bot == null)
                return;
            seat.Bot = null;
            seat.Brain = null;
            _lobbyDirty = true;
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
            _matchElapsed = 0; // reset-score is a fresh match → the clock restarts too (§5.1)
            if (_phase == GamePhase.GameOver)
                RestartLocked();
            _lobbyDirty = true;
        }
    }

    /// <summary>Sets the room's rules. Any seated player may change room options (§3.6).</summary>
    public void SetGameOptions(int userId, bool disallowQuickGoals, bool disallowFirstTouchGoals)
    {
        lock (_gate)
        {
            if (_closed || FindSeatLocked(userId) == null)
                return;
            if (_disallowQuickGoals == disallowQuickGoals && _disallowFirstTouchGoals == disallowFirstTouchGoals)
                return;
            _disallowQuickGoals = disallowQuickGoals;
            _disallowFirstTouchGoals = disallowFirstTouchGoals;
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

    /// <summary>Catch-key state change; ignored from non-seated users. A held catch arms all the
    /// user's rods, so the ball traps on whichever of their figures it reaches (§skill).</summary>
    public void SetCatch(int userId, bool held)
    {
        lock (_gate)
        {
            if (_closed || FindSeatLocked(userId) == null)
                return;
            _catchHeld[userId] = held;
        }
    }

    /// <summary>Lift-key state change for one hand (§skill-lift); ignored from non-seated users.
    /// <paramref name="hand"/> 0 = left (A/D), 1 = right (←/→). The server maps hand → rod(s) from the
    /// seat, so ownership can't be spoofed — you can only lift your own rods. Lifting a rod that holds
    /// the ball frees it (handled in <see cref="GamePhysics"/>), so the key always means "lift".</summary>
    public void SetLift(int userId, int hand, bool held)
    {
        if (hand is not (SeatMap.LeftHand or SeatMap.RightHand))
            return;
        lock (_gate)
        {
            if (_closed || FindSeatLocked(userId) == null)
                return;
            if (!_liftHeld.TryGetValue(userId, out var hands))
                _liftHeld[userId] = hands = new bool[2];
            hands[hand] = held;
        }
    }

    /// <summary>SPACE state change; ignored from non-seated users. Held per user to drive
    /// <see cref="SimState.RodSpace"/> (the goalie's charge). SPACE is context-resolved server-side so
    /// it can't be spoofed: if the caller is holding the trapped ball, the edge drives that ball
    /// (§skill) — on the *press* edge an outfield trap does its lane/back pass, on the *release* edge a
    /// goalie trap launches its charged shot (lane vs back vs launch resolved in <see cref="GamePhysics"/>).
    /// Otherwise (defending, or chasing a loose ball) a press is a dash (§skill-dash).</summary>
    public void SetSpace(int userId, bool held)
    {
        lock (_gate)
        {
            if (_closed || FindSeatLocked(userId) is not { } seat)
                return;
            var was = _spaceHeld.TryGetValue(userId, out var h) && h;
            _spaceHeld[userId] = held;
            if (was == held)
                return; // edges only

            // Holding the ball → SPACE drives it (pass / charge-launch), exactly as before.
            if (_sim.TrappedRod >= 0 && ControlsRodLocked(seat, _sim.TrappedRod))
            {
                var isGoalie = GameConstants.Rods[_sim.TrappedRod].Role == "GK";
                // Goalie: hold to charge, release to launch → fire on the release edge. Outfield: tap to
                // pass → act on the press edge (GamePhysics picks lane vs back).
                if (isGoalie ? !held : held)
                    _sim.PassRequested = true;
                return;
            }

            // No ball in hand → SPACE is a dash: burst this seat's moving rods on the press edge.
            if (held)
                TryDashLocked(seat, userId);
        }
    }

    /// <summary>A dash (§skill-dash): SPACE with no held ball gives this seat's rods that are *currently
    /// moving* a one-off velocity burst in their held direction, on a shared per-user cooldown. Reuses
    /// the rod-velocity ramp — the impulse decays back to normal speed through
    /// <see cref="GamePhysics.IntegrateRods"/>, and because the auto-kick reads live rod velocity,
    /// dashing into a loose ball fires it hard (a fast, steeply-angled, heavily-curved shot). A still
    /// rod has no dash direction, so it stays put; the cooldown starts only if something actually
    /// dashed. Authoritative here — the client predicts the same impulse and the usual snapshot nudge
    /// reconciles any drift.</summary>
    private void TryDashLocked(SeatState seat, int userId)
    {
        if (_dashReadyAt.TryGetValue(userId, out var readyAt) && _time < readyAt)
            return; // still cooling down

        var seatIndex = Array.IndexOf(_seats, seat);
        if (seatIndex < 0)
            return;
        var side = SeatMap.SideOf(seatIndex);
        var alone = SeatsOfSideLocked(side).Count(i => _seats[i].Occupied) == 1;
        _heldInput.TryGetValue(userId, out var hands);

        var dashed = false;
        for (var hand = 0; hand < 2; hand++)
        {
            var dir = hands?[hand] ?? 0;
            if (dir == 0)
                continue; // a still rod has no dash direction
            foreach (var rod in SeatMap.RodsFor(seatIndex, hand, alone))
            {
                _sim.RodVel[rod] = dir * GameConstants.DashSpeed;
                dashed = true;
            }
        }
        if (dashed)
            _dashReadyAt[userId] = _time + GameConstants.DashCooldownSeconds;
    }

    /// <summary>Whether <paramref name="seat"/>'s occupant currently drives <paramref name="rod"/> —
    /// mirrors the hand→rod mapping in <see cref="ApplyRodInputLocked"/> (including the 1v1 pairing).</summary>
    private bool ControlsRodLocked(SeatState seat, int rod)
    {
        var seatIndex = Array.IndexOf(_seats, seat);
        if (seatIndex < 0)
            return false;
        var side = SeatMap.SideOf(seatIndex);
        var alone = SeatsOfSideLocked(side).Count(i => _seats[i].Occupied) == 1;
        for (var hand = 0; hand < 2; hand++)
            if (Array.IndexOf(SeatMap.RodsFor(seatIndex, hand, alone), rod) >= 0)
                return true;
        return false;
    }

    public RoomStateDto GetState()
    {
        lock (_gate)
        {
            return BuildStateLocked();
        }
    }

    /// <summary>Records a seated player's windowed latency report as structured telemetry (§12). The
    /// caller's seat is resolved server-side (the client-sent numbers are display/diagnostic only, never
    /// trusted); dropped if the reporter isn't seated.</summary>
    public void RecordClientStats(int userId, ClientStatsDto stats)
    {
        if (stats?.Rtt is null || stats.Gap is null || stats.Frame is null)
            return;
        int seat;
        lock (_gate)
        {
            seat = Array.FindIndex(_seats, s => s.UserId == userId);
        }
        if (seat < 0)
            return;
        var r = stats.Rtt;
        var g = stats.Gap;
        var f = stats.Frame;
        GameTelemetry.ClientLatency(_logger, TeamId, RoomId, seat,
            r.Mean, r.P50, r.P95, r.Max, r.Count,
            g.Mean, g.P95, g.Max, g.Count,
            f.Mean, f.P95, f.Max,
            stats.ExtrapFrames, stats.SampledFrames);
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

            // Tick-cadence telemetry (§12): actual iteration gap vs the 16.7 ms target, summarized per
            // ~10 s of *playing* and logged. Reveals scheduler/GC/CPU stalls (e.g. a contended Basic-tier
            // instance) that make snapshot pacing jittery. Loop-local — no lock, no cross-tick state.
            const double tickFlushSeconds = 10.0;
            var tickWindow = new SampleWindow(700);
            var sendWindow = new SampleWindow(250); // snapshot broadcast durations — server-side backpressure
            var tickWindowStart = 0.0;
            var tickStalls = 0;

            while (await timer.WaitForNextTickAsync(_cts.Token))
            {
                // PeriodicTimer ticks aren't exact 16.7 ms — measure elapsed time and run a
                // fixed-step accumulator (§4.2), capped so a scheduler stall can't spiral.
                var now = sw.Elapsed.TotalSeconds;
                var gap = now - last;
                acc = Math.Min(acc + gap, 0.25);
                last = now;

                SnapshotDto? snapshot = null;
                var lobbyDirty = false;
                bool destroy;
                bool playingNow;
                int playersNow;
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
                    playingNow = _phase == GamePhase.Playing;
                    playersNow = _seats.Count(s => s.Occupied);
                }

                // Sample cadence only while playing (matches "capture data only when playing"); logged
                // before the awaited sends so a send fault can't drop the window.
                if (playingNow)
                {
                    if (tickWindow.Count == 0)
                        tickWindowStart = now;
                    tickWindow.Add(gap * 1000.0);
                    if (gap > 2 * GameConstants.FixedDt)
                        tickStalls++;
                    if (now - tickWindowStart >= tickFlushSeconds)
                    {
                        var st = tickWindow.Summarize();
                        var ss = sendWindow.Summarize();
                        GameTelemetry.TickCadence(_logger, TeamId, RoomId,
                            st.Mean, st.P95, st.Max, st.Count, tickStalls,
                            ss.Mean, ss.P95, ss.Max, playersNow);
                        tickWindow.Clear();
                        sendWindow.Clear();
                        tickStalls = 0;
                    }
                }

                if (snapshot != null)
                {
                    // Time the broadcast (§12): separates a server-side send stall / slow-client
                    // backpressure from a sim-loop stall — both would otherwise look like "server lag".
                    var sendStart = sw.Elapsed;
                    await _hub.Clients.Group(GroupName).SendAsync("snapshot", snapshot, _cts.Token);
                    if (playingNow)
                        sendWindow.Add((sw.Elapsed - sendStart).TotalMilliseconds);
                }
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
                        _resumeSpin = _sim.BallSpin;
                        _sim.BallVX = 0;
                        _sim.BallVY = 0;
                        _sim.BallSpin = 0;
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

        if (_phase == GamePhase.Playing)
            _matchElapsed += dt;

        ApplyRodInputLocked();

        var result = GamePhysics.Step(_sim, dt);
        if (result.Touched)
        {
            _lastTouch = _time;
            // Count contacts as rising edges — a trap holds contact for many ticks but is one touch;
            // a controlled/trapped ball is never a "first touch" no matter the count (§2.4).
            if (!_touchedLastStep)
                _roundTouchCount++;
            if (_sim.TrappedRod >= 0 || _sim.SlammedThisStep)
                _roundControlled = true; // a trap or a timed drop-slam (§skill-lift) is deliberate, not a fluke
        }
        _touchedLastStep = result.Touched;
        if (result.GoalBySide >= 0)
            HandleGoalLocked(result.GoalBySide);

        if (_phase == GamePhase.Playing && !_sim.BallFrozen)
        {
            // Anti-stall (§2.5): slow for ~5 s or untouched for ~15 s → re-center. A trapped ball is
            // held deliberately (speed 0) and has its own auto-fire timeout, so it must NOT count as
            // stalled — otherwise dribbling/passing for a few seconds force-resets the ball to center.
            if (_sim.TrappedRod >= 0 || _sim.BallSpeed >= GameConstants.StallSpeedThreshold)
            {
                _slowSince = -1;
            }
            else if (_slowSince < 0)
            {
                _slowSince = _time;
            }
            else if (_time - _slowSince >= GameConstants.StallSpeedSeconds)
            {
                ResetBallLocked();
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
            var occupied = seats.Where(i => _seats[i].Occupied).ToArray();

            foreach (var rod in SeatMap.SideRods(side))
            {
                _sim.RodDir[rod] = 0;
                _sim.RodKick[rod] = false;
                _sim.RodSpace[rod] = false;
                _sim.RodLifted[rod] = false;
                _sim.RodGlide[rod] = occupied.Length == 0;
            }
            if (occupied.Length == 0)
                continue;

            var alone = occupied.Length == 1;
            foreach (var seatIndex in occupied)
            {
                var seat = _seats[seatIndex];
                if (seat.Bot is { } difficulty)
                {
                    // Computer seat: each owned rod steers itself toward the ball (§2.3 handles
                    // the kick). Same hand→rod map as a human, so the 1v1 four-rod pairing is free.
                    for (var hand = 0; hand < 2; hand++)
                    {
                        foreach (var rod in SeatMap.RodsFor(seatIndex, hand, alone))
                            _sim.RodDir[rod] = GameBot.DecideRod(_sim, rod, difficulty, _time, _rng, seat.Brain!);
                    }
                    continue;
                }

                var userId = seat.UserId!.Value;
                if (!IsConnectedLocked(userId))
                    continue;
                _heldInput.TryGetValue(userId, out var hands);
                // One catch flag arms every rod this user drives — catch on any of your own figures.
                var catching = _catchHeld.TryGetValue(userId, out var c) && c;
                var spaceDown = _spaceHeld.TryGetValue(userId, out var sp) && sp;
                // Lift is per-hand (§skill-lift), so each hand's rod(s) raise independently. The physics
                // forces the trapped rod down regardless (a man gripping the ball can't lift).
                _liftHeld.TryGetValue(userId, out var lift);
                for (var hand = 0; hand < 2; hand++)
                {
                    foreach (var rod in SeatMap.RodsFor(seatIndex, hand, alone))
                    {
                        if (hands != null)
                            _sim.RodDir[rod] = hands[hand];
                        _sim.RodKick[rod] = catching;
                        _sim.RodSpace[rod] = spaceDown;
                        _sim.RodLifted[rod] = lift != null && lift[hand];
                    }
                }
            }
        }
    }

    private void HandleGoalLocked(int scoringSide)
    {
        // Let rules: a goal that is too cheap is waved off — no score, redo the kickoff neutrally so
        // nobody can cash in before the defense can react (§2.4). Both are gated to a live kickoff
        // (a reconnect resume sets _roundLiveSince to -∞ so a shot already in flight still counts).
        //  • Quick goal: scored within the opening grace window.
        //  • First touch: scored off ≤ FirstTouchMaxContacts uncontrolled figure contacts (0 = served
        //    straight in, 1 = a one-touch finish) — catches the cheap goals that outlast the grace
        //    window. A deliberately trapped/controlled ball is exempt, so real trap-and-shoot counts.
        var quickGoal = _disallowQuickGoals && _time - _roundLiveSince < GameConstants.QuickGoalGraceSeconds;
        var firstTouchGoal = _disallowFirstTouchGoals && !double.IsNegativeInfinity(_roundLiveSince)
            && !_roundControlled && _roundTouchCount <= GameConstants.FirstTouchMaxContacts;
        if (quickGoal || firstTouchGoal)
        {
            ParkBallLocked(Pending.KickRandom);
            _pauseUntil = _time + GameConstants.KickoffPauseSeconds;
            return;
        }

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
                .Where(i => _seats[i].Occupied)
                .Select(i => { var (name, avatar) = DisplayOf(_seats[i]); return new WinnerDto(name, avatar); })
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
        // A fresh kickoff opens a quick-goal grace window; a reconnect resume just continues the
        // round in flight, so it opts out (a shot already on target must still count).
        _roundLiveSince = _pending == Pending.Resume ? double.NegativeInfinity : _time;
        // New round → contacts and control start fresh for the first-touch rule.
        _roundTouchCount = 0;
        _roundControlled = false;
        _touchedLastStep = false;
        switch (_pending)
        {
            case Pending.Resume:
                _sim.BallVX = _resumeVX;
                _sim.BallVY = _resumeVY;
                _sim.BallSpin = _resumeSpin;
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
        _sim.BallSpin = 0;
        _sim.BallFrozen = true;
        _sim.IgnoreRod = -1;
        _sim.IgnoreFigure = -1;
        _sim.TrappedRod = -1;
        _sim.TrappedFigure = -1;
        _sim.ChargeSeconds = 0;
        _sim.HoldSeconds = 0;
        _sim.PassRequested = false;
        _sim.OneTimerArmed = false;
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
        _matchElapsed = 0;
        ParkBallLocked(Pending.KickRandom);
    }

    private void VacateLocked(SeatState seat)
    {
        if (seat.UserId is int userId)
        {
            _heldInput.Remove(userId);
            _catchHeld.Remove(userId);
            _spaceHeld.Remove(userId);
            _liftHeld.Remove(userId);
            _dashReadyAt.Remove(userId);
        }
        seat.UserId = null;
        seat.GraceUntil = null;
        seat.Bot = null;
        seat.Brain = null;
    }

    private SeatState? FindSeatLocked(int userId) =>
        _seats.FirstOrDefault(s => s.UserId == userId);

    private bool IsConnectedLocked(int userId) =>
        _users.TryGetValue(userId, out var session) && session.Connections.Count > 0;

    /// <summary>Display name + avatar for a seat's occupant — the claimed player for a human, or
    /// the generic robot identity for a bot.</summary>
    private static (string Name, int AvatarId) DisplayOf(SeatState seat) =>
        seat.Bot != null ? ("Computer", BotAvatarId) : (seat.Name, seat.AvatarId);

    private static int[] SeatsOfSideLocked(int side) =>
        side == 0 ? [SeatMap.ADefense, SeatMap.AAttack] : [SeatMap.BDefense, SeatMap.BAttack];

    private bool SideOccupiedLocked(int side) =>
        SeatsOfSideLocked(side).Any(i => _seats[i].Occupied);

    /// <summary>Ball in play needs at least one *ready* occupant on the side: a connected human,
    /// or a bot (which is always connected/ready). A grace-held human keeps their seat but doesn't
    /// count (§3.4).</summary>
    private bool SideReadyLocked(int side) =>
        SeatsOfSideLocked(side).Any(i =>
            _seats[i].Bot != null || (_seats[i].UserId is int u && IsConnectedLocked(u)));

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
        _sim.LastKickTick,
        (int)_matchElapsed,
        _sim.TrappedRod,
        _sim.TrappedFigure,
        // Hold-timer ring: 1 when just caught, draining to 0 at the auto-fire timeout (resets to 1 on
        // each pass). Uses HoldSeconds — the always-ticking backstop clock — so it drains even when the
        // goalie is holding without charging. The goalie's window is longer, so it drains slower.
        Math.Round(Math.Clamp(
            1 - _sim.HoldSeconds / (_sim.TrappedRod >= 0
                ? GameConstants.TrapTimeout(_sim.TrappedRod)
                : GameConstants.TrapTimeoutSeconds), 0, 1), 2),
        // Shot-power ring: 0 (regular strength) filling to 1 (near-max cannon) as the charge builds
        // over the trapped rod's charge window — the goalie's strength meter, driven by holding SPACE (§skill).
        _sim.TrappedRod >= 0
            ? Math.Round(Math.Clamp(_sim.ChargeSeconds / GameConstants.MaxCharge(_sim.TrappedRod), 0, 1), 2)
            : 0,
        // Ball spin (english) — lets the client render the ball visibly spinning (§skill).
        Math.Round(_sim.BallSpin, 3),
        // Last pass tick — lets the client play a distinct pass sound (no swing) (§skill).
        _sim.LastPassTick,
        // Goalie aim angle (§skill-aim), radians off straight — the client draws the launch arrow for
        // everyone so opponents can read the charged shot and slide to cover. 0 unless a goalie aims.
        Math.Round(_sim.AimAngle, 3),
        // Lifted-rod bitmask (§skill-lift): bit i set = rod i's men are up, so every client draws them
        // raised (translucent). Excludes the trapped rod (a held ball can't lift).
        LiftedMask(),
        // Anti-stall dismissal warning (§2.5), 0..1 — how close the ball is to an inactivity re-center,
        // so the client can ring it before it vanishes.
        DismissWarnLocked());

    private RoomStateDto BuildStateLocked() => new(
        RoomId,
        _scoreA,
        _scoreB,
        (int)_phase,
        _closed,
        _seats.Select((s, i) => new SeatDto(
            i,
            s.UserId,
            s.Occupied ? DisplayOf(s).Name : null,
            s.Occupied ? DisplayOf(s).AvatarId : null,
            s.Bot != null || (s.UserId is int u && IsConnectedLocked(u)),
            s.Bot != null,
            (int)(s.Bot ?? 0))).ToList(),
        _users
            .Where(kv => kv.Value.Connections.Count > 0 && FindSeatLocked(kv.Key) == null)
            .Select(kv => new ViewerDto(kv.Key, kv.Value.Name, kv.Value.AvatarId))
            .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ToList(),
        _winners,
        new GameOptionsDto(_disallowQuickGoals, _disallowFirstTouchGoals));

    /// <summary>Bitmask of rods whose men are currently up (§skill-lift), bit i = rod i. Mirrors the
    /// physics' effective-lift rule (a trapped rod can't lift), so clients draw exactly what collides.</summary>
    private int LiftedMask()
    {
        var mask = 0;
        for (var i = 0; i < 8; i++)
            if (_sim.RodLifted[i] && i != _sim.TrappedRod)
                mask |= 1 << i;
        return mask;
    }

    /// <summary>How close the ball is to an anti-stall dismissal (§2.5): 0 when not imminent, rising to 1
    /// at the reset, over the final <see cref="GameConstants.StallWarningSeconds"/>. Only while a live,
    /// untrapped ball is actually running down a stall timer — mirrors the reset conditions in the tick
    /// loop, so the ring shows exactly when a re-center is about to fire.</summary>
    private double DismissWarnLocked()
    {
        if (_phase != GamePhase.Playing || _sim.BallFrozen || _sim.TrappedRod >= 0)
            return 0;
        var until = _lastTouch + GameConstants.StallUntouchedSeconds - _time;
        if (_slowSince >= 0)
            until = Math.Min(until, _slowSince + GameConstants.StallSpeedSeconds - _time);
        return until >= GameConstants.StallWarningSeconds
            ? 0
            : Math.Round(Math.Clamp(1 - until / GameConstants.StallWarningSeconds, 0, 1), 2);
    }

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
