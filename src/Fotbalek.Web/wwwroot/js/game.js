// Live game canvas client (spec §4.4): a dumb terminal over the game hub — receives
// snapshots + roomState, interpolates, renders with rAF, captures keyboard input and
// predicts the two own rods locally. Zero game rules live here.

const INTERP_DELAY_MS = 85;       // render this far behind server time (§4.4). ~2.5 snapshot
                                  // intervals at the 30 Hz snapshot rate (33 ms each) — same
                                  // jitter buffer as the old 125 ms @ 20 Hz, but ~40 ms less
                                  // ball lag, so you have to lead a moving ball much less.
const MAX_EXTRAPOLATION_MS = 100; // cap ball extrapolation past the newest snapshot
const NUDGE_RATE = 4;             // /s — correction of predicted own rods by time-aligned error
const PAD = 30;                   // logical padding around the playfield (goal pockets, handles)
const LIFT_FIGURE_ALPHA = 0.28;   // opacity of a lifted rod's men (§skill-lift) — raised out of the plane
const LIFT_SHAKE_PX = 2;          // max rod tremble while a drop-slam charges (§skill-lift), scaled by charge

// Latency instrumentation (§12) — active only while seated and the game is actually playing.
const RTT_PING_INTERVAL_MS = 2000; // how often to probe hub round-trip time
const STATS_WINDOW_MS = 10000;     // window over which samples are summarized and reported

// Mirrors SeatMap.cs: [seat][hand] → rod, and the 1v1 pairs [side][hand] → rods (§2.2).
// Hands are screen-relative — hand 0 (W/S) always drives the rod(s) nearer the left edge.
const OWN_RODS = [[0, 1], [3, 5], [6, 7], [2, 4]];
const PAIR_RODS = [[[0, 1], [3, 5]], [[2, 4], [6, 7]]];
const SIDE_COLORS = ['#ffc107', '#0d6efd'];  // A yellow, B blue (§5.1)
const SIDE_DARK = ['#c79100', '#0a53be'];    // head + swinging foot shades
const SWING_MS = 300;                        // kick swing animation length
const FOOT_EXTENT = 24;                      // how far the foot sweeps from the rod
const DASH_FLASH_MS = 220;                   // how long own rods glow after a dash (§skill-dash), client-only

// Game-feel / juice (Layer 1) — all client-only, no effect on the simulation.
const TRAIL_MS = 140;                        // how long a ball position lingers in the motion trail
const TRAIL_MIN_SPEED = 260;                 // below this the ball leaves no trail (a slow roll shouldn't streak)
const LANE_PASS_TRAIL_MS = 360;              // a lane pass carries the ball (velocity 0), so trail it this long from the hop
                                             // — covers the widest slide (DEF spacing 260 @ 900 u/s ≈ 290 ms), with margin
const SHAKE_DECAY = 13;                      // /s exponential decay of the screen-shake amplitude
const SHAKE_MAX = 9;                         // clamp on shake amplitude (logical units) so a big hit can't lurch
const SPEED_REF = 1200;                      // reference ball speed for normalizing impact intensity 0..1
// Fixed surface markers on the unit ball; rotated by the accumulated roll so the ball looks like it
// spins as it travels. Front hemisphere (rotated z > 0) is drawn, fading toward the silhouette.
const BALL_MARKERS = [
    [0, 0, 1], [0, 0, -1],
    [0.85, 0, 0.5], [-0.85, 0, 0.5],
    [0, 0.85, 0.5], [0, -0.85, 0.5],
];

let canvas = null;
let ctx = null;
let connection = null;
let config = null;
let roomState = null;
let closed = false;
let disposed = false;
let rafHandle = 0;
let resizeObserver = null;
let overlayEl = null;
let opts = null;

// Interpolation timeline (server-tick based, jitter-proof — §4.3).
let snapshots = [];
let clockOffset = null; // smoothed (serverTimeMs - performance.now())
let lastScore = null;
let flash = null;       // { text, color, until }
let swings = [];        // { rod, fig, tMs } — recent auto-kicks, on the server timeline
let lastKickTick = -1;
let lastPassTick = -1;  // last pass tick seen (SnapshotDto.pt) — fires the pass sound on change

// Game-feel state (Layer 1) — all reset in init().
let ballAngX = 0, ballAngY = 0, ballAngZ = 0; // accumulated ball roll/spin for the surface markers
let lastBallX = null, lastBallY = null;        // previous rendered ball position (drives the roll)
let lastResetCounter = null;                   // detect teleports → reset roll/trail so nothing streaks across
let trail = [];                                // { x, y, t } recent rendered ball positions (motion streak)
let particles = [];                            // { x, y, vx, vy, born, life, r, color } transient impact bits
let shake = 0;                                 // current screen-shake amplitude (logical units), decays each frame
let prevSnapV = null;                          // previous snapshot ball velocity — detects wall bounces
let prevSnapR = null;                          // previous snapshot reset counter — skip FX across teleports
let laneTrailUntil = 0;                        // render-clock deadline: trail the carried ball after a lane-pass hop
let seenTrapRod = -1, seenTrapFig = -1;        // render-timeline trap state, to detect the lane-pass figure change
let reduceMotion = false;                      // honor prefers-reduced-motion: no screen shake
let audioCtx = null;                           // lazily created on first sound (WebAudio, synthesized — no assets)

// Input & own-rod prediction.
const held = [{ up: false, down: false }, { up: false, down: false }];
const sentDir = [0, 0];
const catchKeys = new Set(); // currently-pressed catch keys (any Shift / alternate)
let sentCatch = false;
let sentSpace = false; // last SPACE held-state sent (down/up edges only)
const liftHeld = [false, false]; // per-hand lift state (A/D, ←/→) — §skill-lift; also what's been sent
const liftStart = [0, 0];        // performance.now() each hand's lift began — local drop-slam charge readout
const liftKeysDown = new Set();  // currently-pressed lift keys (a hand stays lifted while either of its keys is down)
let myRods = null;      // { hands: [rodIdxs, rodIdxs] } when seated, else null
let mySide = null;      // 0 / 1 when seated (drives the win-vs-lose match-end fanfare), null for a viewer
let predicted = null;   // rodIdx → offset
let predHistory = {};   // rodIdx → [{ t, o }] — past predictions on the server timeline
let predVel = {};       // rodIdx → world units/s — persistent ramp velocity, mirrors SimState.RodVel
let dashReadyAt = 0;    // performance.now() clock: earliest time a local dash is allowed (mirrors the
                        // server per-user cooldown, so we don't predict a dash the server will reject)
let dashFlashUntil = 0; // render-clock deadline for the own-rod dash glow (client-only juice)
let lastFrameTime = 0;

// Latency instrumentation (§12): per-window sample buffers + timers, all in ms on the client clock.
let currentPhase = 0;      // last seen snapshot state (0 waiting / 1 playing / 2 game over)
let statsRtt = [];         // hub round-trip samples in the current window
let statsGap = [];         // snapshot inter-arrival samples in the current window
let statsFrame = [];       // render frame-interval samples (device/tab jank)
let extrapFrames = 0;      // frames this window with no future snapshot (had to extrapolate the ball)
let sampledFrames = 0;     // total frames sampled this window (extrap fraction = extrapFrames/sampledFrames)
let lastSnapAt = 0;        // performance.now() of the previous snapshot (0 = none yet)
let lastPingAt = 0;        // performance.now() of the last RTT probe
let lastStatsFlushAt = 0;  // performance.now() of the last window flush (0 = not measuring)
let pingInFlight = false;  // avoid overlapping RTT probes
let displayPing = null;    // smoothed RTT (ms) for the on-canvas readout; null until first probe

// Player options — client-local display preferences (NOT room/game state), persisted per browser.
// Extensible: add a key + default here and a toggle in LiveGame.razor; game.js reads them each frame.
const PLAYER_OPTIONS_KEY = 'fotbalek.playerOptions';
let playerOptions = { showPing: false, sound: false };

export async function init(canvasEl, options) {
    disposed = false;
    closed = false;
    canvas = canvasEl;
    ctx = canvas.getContext('2d');
    opts = options;
    snapshots = [];
    clockOffset = null;
    lastScore = null;
    flash = null;
    swings = [];
    lastKickTick = -1;
    lastPassTick = -1;
    myRods = null;
    mySide = null;
    predicted = null;
    predHistory = {};
    predVel = {};
    dashReadyAt = 0;
    dashFlashUntil = 0;
    lastLayoutKey = '';
    lastFrameTime = 0;
    ballAngX = ballAngY = ballAngZ = 0;
    lastBallX = lastBallY = null;
    lastResetCounter = null;
    trail = [];
    particles = [];
    shake = 0;
    prevSnapV = null;
    prevSnapR = null;
    laneTrailUntil = 0;
    seenTrapRod = seenTrapFig = -1;
    try { reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches; } catch { reduceMotion = false; }
    for (const hand of [0, 1]) {
        held[hand].up = held[hand].down = false;
        sentDir[hand] = 0;
        liftHeld[hand] = false;
        liftStart[hand] = 0;
    }
    catchKeys.clear();
    liftKeysDown.clear();
    sentCatch = false;
    sentSpace = false;
    currentPhase = 0;
    statsRtt = [];
    statsGap = [];
    statsFrame = [];
    extrapFrames = 0;
    sampledFrames = 0;
    lastSnapAt = 0;
    lastPingAt = 0;
    lastStatsFlushAt = 0;
    pingInFlight = false;
    displayPing = null;
    loadPlayerOptions(); // pick up this browser's persisted toggles (kept across re-inits)

    // Vendored UMD build (mirrors vendored Bootstrap — no bundler, no CDN); it attaches
    // itself to the global scope, dynamic import just executes it once.
    if (!window.signalR) {
        await import('/lib/signalr/signalr.min.js');
    }
    const signalR = window.signalR;

    // Default automatic-reconnect gives up after ~42 s while a 30 s seat grace may still be
    // running — retry steadily every 2 s for ~60 s instead (§4.4).
    const retryDelays = [0, ...Array(30).fill(2000)];
    connection = new signalR.HubConnectionBuilder()
        .withUrl(opts.hubUrl)
        .withAutomaticReconnect(retryDelays)
        .build();

    connection.on('snapshot', onSnapshot);
    connection.on('roomState', onRoomState);
    connection.onreconnecting(() => { displayPing = null; showOverlay('Reconnecting…', false); });
    connection.onreconnected(async () => {
        // A reconnected connection is brand new to the server — group membership does not
        // survive it; re-join and re-send held-key state (§3.5). Score and kick baselines
        // reset too, so a goal scored or kick made while away doesn't flash/replay on rejoin.
        hideOverlay();
        snapshots = [];
        clockOffset = null;
        lastScore = null;
        lastKickTick = -1;
        lastPassTick = -1;
        // Impact/trail FX baselines too, so the first post-reconnect snapshot can't fire a stale
        // wall-bounce (velocity vs a pre-disconnect reading) or streak the trail across the jump.
        prevSnapV = null;
        prevSnapR = null;
        trail = [];
        lastBallX = lastBallY = null;
        lastSnapAt = 0;   // don't count the reconnect gap as snapshot jitter (§12)
        statsRtt = [];
        statsGap = [];
        statsFrame = [];
        extrapFrames = 0;
        sampledFrames = 0;
        const joined = await joinRoom();
        if (joined) {
            resendInput();
        }
    });
    connection.onclose(() => {
        if (!disposed && !closed) {
            showOverlay('Connection lost.', true);
        }
    });

    window.addEventListener('keydown', onKeyDown);
    window.addEventListener('keyup', onKeyUp);
    window.addEventListener('blur', releaseAllKeys);
    document.addEventListener('visibilitychange', onVisibilityChange);

    resizeObserver = new ResizeObserver(resizeCanvas);
    resizeObserver.observe(canvas.parentElement);
    resizeCanvas();

    rafHandle = requestAnimationFrame(frame);

    try {
        await connection.start();
    } catch {
        // Automatic reconnect does not cover initial-start failure (§4.4).
        showOverlay('Could not connect.', true);
        return;
    }
    const joined = await joinRoom();
    if (!joined && !closed) {
        showOverlay('Game not found — it may have ended.', true);
    }
}

export async function dispose() {
    if (disposed) {
        return;
    }
    disposed = true;
    cancelAnimationFrame(rafHandle);
    window.removeEventListener('keydown', onKeyDown);
    window.removeEventListener('keyup', onKeyUp);
    window.removeEventListener('blur', releaseAllKeys);
    document.removeEventListener('visibilitychange', onVisibilityChange);
    resizeObserver?.disconnect();
    resizeObserver = null;
    hideOverlay();
    const conn = connection;
    connection = null;
    if (conn) {
        try { await conn.stop(); } catch { /* already gone */ }
    }
}

async function joinRoom() {
    try {
        const result = await connection.invoke('JoinRoom', opts.roomId);
        if (!result) {
            return false;
        }
        config = result.config;
        applyRoomState(result.state);
        onSnapshot(result.snapshot);
        return true;
    } catch {
        return false;
    }
}

// ---- Incoming state ------------------------------------------------------------------------

function onSnapshot(snap) {
    if (!snap || !config) {
        return;
    }

    // Snapshot inter-arrival gap (§12) — measured as the client sees it, so it captures network jitter
    // (and any server pacing stalls). Recorded only while actively playing; phase is set first so the
    // measuring() gate below reflects this very snapshot.
    const arrival = performance.now();
    const prevPhase = currentPhase; // captured before the update — drives the match-end fanfare below
    currentPhase = snap.st;
    if (measuring() && lastSnapAt > 0) {
        statsGap.push(arrival - lastSnapAt);
    }
    lastSnapAt = arrival;

    const serverTime = snap.t * (1000 / config.tickRate);
    const sample = serverTime - performance.now();
    clockOffset = clockOffset === null ? sample : clockOffset + (sample - clockOffset) * 0.1;

    snapshots.push(snap);
    if (snapshots.length > 60) {
        snapshots.splice(0, snapshots.length - 60);
    }

    // New auto-kick → queue the figure's swing at its exact server time, and fire impact juice
    // (particles + shake + sound at the ball). The first snapshot (join/rejoin) only sets the
    // baseline so an old kick doesn't replay; kickedThisSnap gates the wall-bounce check below.
    let kickedThisSnap = false;
    if (snap.k) {
        if (lastKickTick < 0) {
            lastKickTick = snap.kt;
        } else if (snap.kt > lastKickTick) {
            lastKickTick = snap.kt;
            if (snap.k[0] >= 0) {
                kickedThisSnap = true;
                swings.push({ rod: snap.k[0], fig: snap.k[1], tMs: snap.kt * (1000 / config.tickRate) });
                if (swings.length > 12) {
                    swings.shift();
                }
                const intensity = speedIntensity(snap.v[0], snap.v[1]);
                spawnParticles(snap.b[0], snap.b[1], 8, 130 * intensity, 'rgba(255,255,255,0.9)', 260);
                addShake(3.5 * intensity);
                playKick(intensity);
            }
        }
    }

    // New pass (lane-pass hop or back-pass toss) → a soft, airy pass sound, distinct from a kick. No
    // swing/particles/shake — a pass is a quiet, controlled move. Baseline-only on the first snapshot.
    if (snap.pt !== undefined) {
        if (lastPassTick < 0) {
            lastPassTick = snap.pt;
        } else if (snap.pt > lastPassTick) {
            lastPassTick = snap.pt;
            playPass();
        }
    }

    // Wall bounce → a soft tick + dust where the ball met the cushion, detected as a velocity-sign
    // flip between snapshots. A flip from a kick is excluded (handled above), teleports are skipped,
    // and an x-flip only counts near an end wall so a mid-table strike can't masquerade as a bounce.
    if (prevSnapV && !kickedThisSnap && snap.r === prevSnapR) {
        const W = config.width, H = config.height, r = config.ballRadius;
        const intensity = speedIntensity(snap.v[0], snap.v[1]);
        if (Math.sign(snap.v[1]) !== Math.sign(prevSnapV[1]) && Math.abs(prevSnapV[1]) > 40
            && (snap.b[1] < r + 26 || snap.b[1] > H - r - 26)) {
            spawnParticles(snap.b[0], snap.b[1] < H / 2 ? r : H - r, 5, 90 * intensity, 'rgba(220,235,255,0.85)', 200);
            addShake(1.6 * intensity);
            playWall(intensity);
        } else if (Math.sign(snap.v[0]) !== Math.sign(prevSnapV[0]) && Math.abs(prevSnapV[0]) > 40
            && (snap.b[0] < r + 26 || snap.b[0] > W - r - 26)) {
            spawnParticles(snap.b[0] < W / 2 ? r : W - r, snap.b[1], 5, 90 * intensity, 'rgba(220,235,255,0.85)', 200);
            addShake(1.6 * intensity);
            playWall(intensity);
        }
    }

    // GOAL flash + celebration on a score increment; a decrease is a reset/rematch — no flash (§5.1).
    if (lastScore) {
        let scoringSide = -1;
        if (snap.s[0] > lastScore[0]) {
            scoringSide = 0;
        } else if (snap.s[1] > lastScore[1]) {
            scoringSide = 1;
        }
        if (scoringSide >= 0) {
            flash = { text: 'GOAL!', color: SIDE_COLORS[scoringSide], until: performance.now() + 1200 };
            // Team A (0) attacks right, so it scores in the right goal; team B in the left.
            const gx = scoringSide === 0 ? config.width : 0;
            spawnParticles(gx, config.height / 2, 30, 320, SIDE_COLORS[scoringSide], 900);
            addShake(8);
            playGoal();
        }
    }

    // Match just ended (a side reached the winning score — no draws) → a fanfare, flavored win vs lose
    // for a seated player. Gated on lastScore so the first snapshot (joining a finished game) is silent,
    // and on the phase transition so it fires exactly once. The winner is the higher final score.
    if (lastScore !== null && prevPhase !== 2 && snap.st === 2) { // 2 = game over
        const winnerSide = snap.s[0] > snap.s[1] ? 0 : 1;
        if (mySide !== null && winnerSide !== mySide) {
            playDefeat();
        } else {
            playVictory(); // a seated winner, or a viewer — a triumphant flourish either way
        }
    }

    lastScore = snap.s;
    prevSnapV = snap.v;
    prevSnapR = snap.r;
}

function onRoomState(state) {
    if (state.closed) {
        closed = true;
        releaseAllKeys();
        myRods = null;
        // The Blazor page shows the "game ended" notice; just stop the wire.
        connection?.stop();
        return;
    }
    applyRoomState(state);
}

function applyRoomState(state) {
    roomState = state;
    const mySeat = state.seats.find(s => s.userId === opts.userId);
    if (!mySeat) {
        if (myRods) {
            // Seat lost while keys may be held — key-up events are swallowed once unseated,
            // so clear the local mirror too (the server already cleared its copy on vacate).
            releaseAllKeys();
        }
        myRods = null;
        mySide = null;
        predicted = null;
        predHistory = {};
        predVel = {};
        return;
    }
    const side = mySeat.seat <= 1 ? 0 : 1;
    mySide = side;
    const occupiedOnSide = state.seats
        .filter(s => (s.seat <= 1 ? 0 : 1) === side && (s.userId !== null || s.isBot)).length;
    const alone = occupiedOnSide === 1;
    myRods = {
        hands: [0, 1].map(h => alone ? PAIR_RODS[side][h] : [OWN_RODS[mySeat.seat][h]]),
    };
    predicted = predicted ?? {};
    // Drop predictions for rods no longer owned (seat swap, 1v1 pairing change) so a rod
    // regained later starts from the server offset instead of a stale local one.
    const owned = new Set(myRods.hands.flat());
    for (const key of Object.keys(predicted)) {
        if (!owned.has(Number(key))) {
            delete predicted[key];
            delete predHistory[key];
            delete predVel[key];
        }
    }
}

// ---- Input (§2.2, §4.4) ---------------------------------------------------------------------

const KEY_MAP = {
    KeyW: { hand: 0, dir: 'up' },
    KeyS: { hand: 0, dir: 'down' },
    ArrowUp: { hand: 1, dir: 'up' },
    ArrowDown: { hand: 1, dir: 'down' },
};

// Hold to catch/trap the ball on any of your figures — one action, not per-hand. Either Shift works
// (pick whichever frees the hand you want to dribble with); F and / are sticky-keys-free alternates.
const CATCH_KEYS = new Set(['ShiftLeft', 'ShiftRight', 'KeyF', 'Slash']);

// Lift keys (§skill-lift): the horizontal neighbours of each slide cluster raise that hand's rod(s) —
// A/D for the left hand (W/S), ←/→ for the right hand (↑/↓). Either key of a pair lifts, so it's
// forgiving. Deliberately NOT Ctrl (Ctrl+W/Ctrl+S are browser shortcuts on the left hand's slide keys).
const LIFT_KEYS = { KeyA: 0, KeyD: 0, ArrowLeft: 1, ArrowRight: 1 };

function onKeyDown(e) {
    if (!myRods || isTyping(e)) {
        return;
    }
    if (e.code === 'Space') {
        e.preventDefault(); // don't scroll, and don't trigger a focused button
        // Held state: press starts the goalie's charge (and the outfield pass); release launches the
        // goalie shot. Edges only — key-repeat is ignored. When SPACE isn't acting on a ball we're
        // holding, the same press is a dash — predict it locally so the lunge shows instantly.
        if (!e.repeat && !sentSpace) {
            sentSpace = true;
            connection?.send('Space', true).catch(() => { });
            maybeDash();
        }
        return;
    }
    if (CATCH_KEYS.has(e.code)) {
        e.preventDefault();
        if (!e.repeat) { // held state is tracked, not repeated keydowns
            catchKeys.add(e.code);
            syncCatch();
        }
        return;
    }
    const liftHand = LIFT_KEYS[e.code];
    if (liftHand !== undefined) {
        e.preventDefault(); // keep ←/→ from scrolling the page
        if (!e.repeat) {
            liftKeysDown.add(e.code);
            setLift(liftHand, true);
        }
        return;
    }
    const map = KEY_MAP[e.code];
    if (!map) {
        return;
    }
    e.preventDefault(); // keep arrows from scrolling, including key-repeat events
    if (e.repeat) {
        return;
    }
    held[map.hand][map.dir] = true;
    syncHand(map.hand);
}

function onKeyUp(e) {
    if (!myRods || isTyping(e)) {
        return;
    }
    if (e.code === 'Space') {
        e.preventDefault();
        if (sentSpace) {
            sentSpace = false;
            connection?.send('Space', false).catch(() => { });
        }
        return;
    }
    if (CATCH_KEYS.has(e.code)) {
        e.preventDefault();
        catchKeys.delete(e.code);
        syncCatch();
        return;
    }
    const liftHand = LIFT_KEYS[e.code];
    if (liftHand !== undefined) {
        e.preventDefault();
        liftKeysDown.delete(e.code);
        // Still lifted if the pair's other key is down (either A/D, either ←/→).
        const stillDown = Object.keys(LIFT_KEYS).some(c => LIFT_KEYS[c] === liftHand && liftKeysDown.has(c));
        setLift(liftHand, stillDown);
        return;
    }
    const map = KEY_MAP[e.code];
    if (!map) {
        return;
    }
    e.preventDefault();
    held[map.hand][map.dir] = false;
    syncHand(map.hand);
}

function isTyping(e) {
    const t = e.target;
    return t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.isContentEditable);
}

function syncHand(hand) {
    const dir = (held[hand].down ? 1 : 0) - (held[hand].up ? 1 : 0);
    if (dir === sentDir[hand]) {
        return;
    }
    sentDir[hand] = dir;
    connection?.send('HandInput', hand, dir).catch(() => { });
}

function syncCatch() {
    const isHeld = catchKeys.size > 0;
    if (isHeld === sentCatch) {
        return;
    }
    sentCatch = isHeld;
    connection?.send('Catch', isHeld).catch(() => { });
}

/** Send a hand's lift state on its edges (§skill-lift), and stamp the charge clock when it goes up so
 *  the local power readout can grow from the moment of the press. */
function setLift(hand, held) {
    if (held === liftHeld[hand]) {
        return;
    }
    liftHeld[hand] = held;
    if (held) {
        liftStart[hand] = performance.now();
    }
    connection?.send('Lift', hand, held).catch(() => { });
}

/** Optimistically mirror the server dash (§skill-dash): SPACE with no ball in hand bursts our moving
 *  rods' predicted velocity so the lunge appears instantly, exactly as the server will. Seeds predVel
 *  with the same DashSpeed the server uses — from there the shared ramp in predictOwnRods decays it
 *  identically, and the snapshot nudge corrects any drift. The cooldown mirrors the server's so we
 *  don't show a dash it rejects; the server stays authoritative. */
function maybeDash() {
    if (!myRods || !config || !config.dashSpeed) {
        return;
    }
    const now = performance.now();
    if (now < dashReadyAt) {
        return; // still cooling down
    }
    // If we're holding the trapped ball, SPACE is a pass/launch, not a dash — leave it to that path.
    const trapRod = snapshots.length ? snapshots[snapshots.length - 1].tr : -1;
    if (trapRod >= 0 && myRods.hands.flat().includes(trapRod)) {
        return;
    }
    let dashed = false;
    for (const hand of [0, 1]) {
        const dir = sentDir[hand];
        if (dir === 0) {
            continue; // a still rod has no dash direction
        }
        for (const rodIdx of myRods.hands[hand]) {
            predVel[rodIdx] = dir * config.dashSpeed;
            dashed = true;
        }
    }
    if (dashed) {
        dashReadyAt = now + config.dashCooldownSeconds * 1000;
        dashFlashUntil = now + DASH_FLASH_MS;
        playDash(); // airy whoosh — audio confirmation to match the cyan rod glow
    }
}

function releaseAllKeys() {
    for (const hand of [0, 1]) {
        held[hand].up = held[hand].down = false;
        syncHand(hand); // a backgrounded tab must not leave a rod gliding into the wall
    }
    catchKeys.clear();
    syncCatch(); // …nor holding a ball trapped
    liftKeysDown.clear();
    for (const hand of [0, 1]) {
        setLift(hand, false); // …nor leave a rod lifted (its men stuck up, goal exposed)
    }
    if (sentSpace) { // …nor leave the goalie charging / the ball unlaunched
        sentSpace = false;
        connection?.send('Space', false).catch(() => { });
    }
}

function onVisibilityChange() {
    if (document.hidden) {
        releaseAllKeys();
    }
}

function resendInput() {
    for (const hand of [0, 1]) {
        if (sentDir[hand] !== 0) {
            connection?.send('HandInput', hand, sentDir[hand]).catch(() => { });
        }
    }
    if (sentCatch) {
        connection?.send('Catch', true).catch(() => { });
    }
    for (const hand of [0, 1]) {
        if (liftHeld[hand]) {
            connection?.send('Lift', hand, true).catch(() => { });
        }
    }
    if (sentSpace) {
        connection?.send('Space', true).catch(() => { });
    }
}

// ---- Interpolation & prediction ---------------------------------------------------------------

/** Ball position/velocity and rod offsets at render time (~85 ms behind server). */
function sampleWorld(nowMs) {
    if (snapshots.length === 0 || clockOffset === null) {
        return null;
    }
    const tickMs = 1000 / config.tickRate;
    const renderTime = nowMs + clockOffset - INTERP_DELAY_MS;

    let a = snapshots[0];
    let b = null;
    for (let i = snapshots.length - 1; i >= 0; i--) {
        if (snapshots[i].t * tickMs <= renderTime) {
            a = snapshots[i];
            b = snapshots[i + 1] ?? null;
            break;
        }
    }

    if (b && b.r !== a.r) {
        // Ball teleported between the two snapshots — snap, don't interpolate across it (§4.3).
        return { ball: [b.b[0], b.b[1]], rods: b.o, snap: b, renderTime };
    }
    if (b) {
        const ta = a.t * tickMs;
        const tb = b.t * tickMs;
        const alpha = tb > ta ? Math.min(1, Math.max(0, (renderTime - ta) / (tb - ta))) : 1;
        return {
            ball: [a.b[0] + (b.b[0] - a.b[0]) * alpha, a.b[1] + (b.b[1] - a.b[1]) * alpha],
            rods: a.o.map((o, i) => o + (b.o[i] - o) * alpha),
            snap: b,
            renderTime,
        };
    }
    // Past the newest snapshot — extrapolate the ball briefly from its velocity (§4.3). Flagged so the
    // frame loop can track how often the interpolation buffer runs dry (§12).
    const newest = snapshots[snapshots.length - 1];
    const dt = Math.min(Math.max(0, renderTime - newest.t * tickMs), MAX_EXTRAPOLATION_MS) / 1000;
    return {
        ball: [newest.b[0] + newest.v[0] * dt, newest.b[1] + newest.v[1] * dt],
        rods: newest.o,
        snap: newest,
        renderTime,
        extrapolated: true,
    };
}

/** 0 = foot hidden under the body; sweeps from behind (-) through the ball to the front (+)
 *  and back — the top-down view of the man rotating around his rod. */
function swingFoot(p) {
    if (p < 0.35) {
        return -0.7 + 1.7 * (p / 0.35);
    }
    if (p < 0.6) {
        return 1;
    }
    return 1 - (p - 0.6) / 0.4;
}

function footValue(rodIdx, figIdx, renderTime) {
    for (let i = swings.length - 1; i >= 0; i--) {
        const s = swings[i];
        if (s.rod === rodIdx && s.fig === figIdx) {
            const p = (renderTime - s.tMs) / SWING_MS;
            if (p >= 0 && p <= 1) {
                return swingFoot(p);
            }
        }
    }
    return 0;
}

/** Own rods render from locally integrated key state, reconciled against the server (§4.4).
 *  The server offset we can see (`rods`) is ~INTERP_DELAY_MS old, so correcting the live
 *  prediction toward it directly would rubber-band: a tap snaps back and glides up as the
 *  delayed stream catches up. Instead we compare like with like — the server value at
 *  `renderTime` against what we predicted for that same instant (kept in `predHistory`) — and
 *  fold only that error in. When the server confirms our input the error is ~0 (no spring);
 *  genuine drift (missed input, wall clamp, pairing change) still corrects smoothly. */
function predictOwnRods(rods, dtSec, renderTime) {
    if (!myRods || !predicted) {
        return rods;
    }
    const out = rods.slice();
    const nudge = 1 - Math.exp(-NUDGE_RATE * dtSec);
    const serverNow = renderTime + INTERP_DELAY_MS; // server time this prediction represents
    // A goalie we control freezes while charging its aimed shot (§skill-aim) — ↑/↓ swing the aim, not
    // the rod. Mirror the server's freeze rule (GamePhysics.IntegrateRods) so the held rod doesn't drift.
    const latest = snapshots.length ? snapshots[snapshots.length - 1] : null;
    const aimRod = sentSpace && latest && latest.tr >= 0
        && config.rods[latest.tr] && config.rods[latest.tr].figures === 1
        && myRods.hands.flat().includes(latest.tr) ? latest.tr : -1;
    for (const hand of [0, 1]) {
        for (const rodIdx of myRods.hands[hand]) {
            const rod = config.rods[rodIdx];
            // Mirror the server rod ramp (GamePhysics.IntegrateRods) exactly: ease this rod's speed
            // toward the held-direction target, then integrate position. Keeping the two integrators
            // identical is what keeps the prediction from rubber-banding against the server stream.
            const dir = rodIdx === aimRod ? 0 : sentDir[hand]; // aiming goalie holds still (§skill-aim)
            const target = dir * config.rodSpeed;
            let v = predVel[rodIdx] ?? 0;
            const speedingUp = dir !== 0 && (v === 0 || Math.sign(v) === Math.sign(target));
            const rate = speedingUp ? config.rodAccel : config.rodDecel;
            v = moveToward(v, target, rate * dtSec);
            if (rodIdx === aimRod) {
                v = 0; // server hard-freezes the aiming keeper (RodVel=0, position held) — don't coast
            }             // through a decel ramp here or the rod over-travels until the nudge claws it back.
            let p = predicted[rodIdx] ?? rods[rodIdx];
            p = Math.min(1, Math.max(0, p + (v / rod.travel) * dtSec));
            if (p <= 0 || p >= 1) {
                v = 0; // hit the travel limit — kill momentum, no coast into the wall
            }
            predVel[rodIdx] = v;
            const past = sampleHistory(rodIdx, renderTime);
            if (past !== null) {
                p = Math.min(1, Math.max(0, p + (rods[rodIdx] - past) * nudge));
            }
            predicted[rodIdx] = p;
            recordHistory(rodIdx, serverNow, p);
            out[rodIdx] = p;
        }
    }
    return out;
}

/** Store the prediction for a rod on the server timeline, pruning entries too old to ever be
 *  looked up (renderTime = serverNow - INTERP_DELAY_MS, with margin). */
function recordHistory(rodIdx, t, o) {
    const h = predHistory[rodIdx] ?? (predHistory[rodIdx] = []);
    h.push({ t, o });
    const cutoff = t - INTERP_DELAY_MS - 300;
    let drop = 0;
    while (drop < h.length - 1 && h[drop + 1].t < cutoff) {
        drop++;
    }
    if (drop > 0) {
        h.splice(0, drop);
    }
}

/** What we predicted for this rod at server time `t`, linearly interpolated; null if no
 *  history yet (first frames after sitting — just integrate until it fills). */
function sampleHistory(rodIdx, t) {
    const h = predHistory[rodIdx];
    if (!h || h.length === 0) {
        return null;
    }
    if (t <= h[0].t) {
        return h[0].o;
    }
    if (t >= h[h.length - 1].t) {
        return h[h.length - 1].o;
    }
    for (let i = h.length - 1; i > 0; i--) {
        if (h[i - 1].t <= t) {
            const a = h[i - 1];
            const b = h[i];
            const alpha = b.t > a.t ? (t - a.t) / (b.t - a.t) : 0;
            return a.o + (b.o - a.o) * alpha;
        }
    }
    return h[0].o;
}

// ---- Latency instrumentation (§12) -----------------------------------------------------------

/** Capture only while this client is a seated player AND the game is actively playing, on a live
 *  connection — matches the "collect data only when playing" requirement and keeps volume tiny. */
function measuring() {
    return !!myRods && currentPhase === 1 && !!connection && !closed && !disposed;
}

/** Driven from the rAF loop: paces the RTT probe and the window flush. When measurement stops
 *  (game over, left seat, disconnect) any partial window is flushed so nothing is silently dropped. */
function maybeMeasure(nowMs) {
    if (!connection || closed || disposed) {
        return;
    }
    // Probe RTT whenever it's needed — for telemetry (while playing) OR just to feed the ping readout
    // (any time the player has it toggled on, including as a viewer or while waiting).
    if ((measuring() || playerOptions.showPing) && !pingInFlight && nowMs - lastPingAt >= RTT_PING_INTERVAL_MS) {
        doPing(nowMs);
    }
    if (measuring()) {
        if (!lastStatsFlushAt) {
            lastStatsFlushAt = nowMs; // start the first window now, so it's a full STATS_WINDOW_MS
        }
        if (nowMs - lastStatsFlushAt >= STATS_WINDOW_MS) {
            flushStats();
            lastStatsFlushAt = nowMs;
        }
    } else if (statsRtt.length || statsGap.length || statsFrame.length || sampledFrames) {
        flushStats();
        lastStatsFlushAt = 0;
    }
}

/** No-op hub round trip; the promise resolves once the server acks, so the elapsed time is the real
 *  application-level RTT through SignalR. Feeds the smoothed on-canvas readout always, and the
 *  telemetry window only while playing (so telemetry stays "playing-only"). Failures ignored. */
function doPing(nowMs) {
    pingInFlight = true;
    lastPingAt = nowMs;
    const t0 = performance.now();
    connection.invoke('Ping')
        .then(() => {
            const rtt = performance.now() - t0;
            displayPing = displayPing === null ? rtt : displayPing * 0.7 + rtt * 0.3; // EMA — steady readout
            if (measuring()) {
                statsRtt.push(rtt);
            }
        })
        .catch(() => { })
        .finally(() => { pingInFlight = false; });
}

/** Summarize the current window client-side (Azure Monitor can't derive percentiles from histograms)
 *  and ship one compact report, then reset the buffers. */
function flushStats() {
    if (statsRtt.length === 0 && statsGap.length === 0 && statsFrame.length === 0 && sampledFrames === 0) {
        return;
    }
    const payload = {
        rtt: summarizeStats(statsRtt),
        gap: summarizeStats(statsGap),
        frame: summarizeStats(statsFrame),
        extrapFrames: extrapFrames,
        sampledFrames: sampledFrames,
    };
    statsRtt = [];
    statsGap = [];
    statsFrame = [];
    extrapFrames = 0;
    sampledFrames = 0;
    connection?.invoke('ReportStats', payload).catch(() => { });
}

/** count / min / mean / p50 / p95 / max over a sample array (nearest-rank percentiles). */
function summarizeStats(arr) {
    const n = arr.length;
    if (n === 0) {
        return { count: 0, min: 0, mean: 0, p50: 0, p95: 0, max: 0 };
    }
    const sorted = [...arr].sort((a, b) => a - b);
    const sum = sorted.reduce((a, b) => a + b, 0);
    const pct = p => sorted[Math.min(n - 1, Math.max(0, Math.ceil(p * n) - 1))];
    return { count: n, min: sorted[0], mean: sum / n, p50: pct(0.5), p95: pct(0.95), max: sorted[n - 1] };
}

// ---- Player options (client-local, §12) ------------------------------------------------------

function loadPlayerOptions() {
    try {
        const raw = localStorage.getItem(PLAYER_OPTIONS_KEY);
        if (raw) {
            playerOptions = { ...playerOptions, ...JSON.parse(raw) };
        }
    } catch { /* private mode / disabled storage — fall back to defaults */ }
}

/** Read one option (typed as bool) — used by the Blazor panel to render the toggle's initial state. */
export function getPlayerOption(key) {
    return !!playerOptions[key];
}

/** Set + persist one option. Called from the Blazor toggle via interop (Blazor→JS only, §4.4). */
export function setPlayerOption(key, value) {
    playerOptions[key] = value;
    // Enabling sound happens inside the toggle's click — a user gesture — so create/resume the
    // AudioContext now, or the first in-game sound would be blocked by the autoplay policy.
    if (key === 'sound' && value) {
        ensureAudio();
    }
    try {
        localStorage.setItem(PLAYER_OPTIONS_KEY, JSON.stringify(playerOptions));
    } catch { /* ignore */ }
}

// ---- Game feel: sound + impact FX (Layer 1) --------------------------------------------------

/** WebAudio context, created lazily and only when sound is enabled. Sounds are synthesized (no asset
 *  files, matching the no-CDN/no-bundler setup). Autoplay policy: the context is resumed here, which
 *  works because sounds fire during play (after keydown) or right after the user flips the toggle. */
function ensureAudio() {
    if (!playerOptions.sound) {
        return null;
    }
    if (!audioCtx) {
        const Ctx = window.AudioContext || window.webkitAudioContext;
        if (!Ctx) {
            return null;
        }
        try { audioCtx = new Ctx(); } catch { return null; }
    }
    if (audioCtx.state === 'suspended') {
        audioCtx.resume().catch(() => { });
    }
    return audioCtx;
}

/** A short tone that can slide in pitch — the tonal body of a hit or the goal jingle. `delay`
 *  (seconds) schedules it into the future, so a fanfare can stagger several tones into a melody. */
function tone(freq, dur, type, gain, slideTo, delay = 0) {
    const ac = ensureAudio();
    if (!ac) {
        return;
    }
    const t = ac.currentTime + delay;
    const osc = ac.createOscillator();
    const g = ac.createGain();
    osc.type = type;
    osc.frequency.setValueAtTime(freq, t);
    if (slideTo) {
        osc.frequency.exponentialRampToValueAtTime(slideTo, t + dur);
    }
    g.gain.setValueAtTime(0.0001, t);
    g.gain.exponentialRampToValueAtTime(gain, t + 0.006);
    g.gain.exponentialRampToValueAtTime(0.0001, t + dur);
    osc.connect(g).connect(ac.destination);
    osc.start(t);
    osc.stop(t + dur + 0.02);
}

/** A decaying filtered-noise burst — the "thwack" of contact. `delay` (seconds) schedules it ahead. */
function noise(dur, gain, cutoff, delay = 0) {
    const ac = ensureAudio();
    if (!ac) {
        return;
    }
    const t = ac.currentTime + delay;
    const n = Math.max(1, Math.floor(ac.sampleRate * dur));
    const buf = ac.createBuffer(1, n, ac.sampleRate);
    const data = buf.getChannelData(0);
    for (let i = 0; i < n; i++) {
        data[i] = (Math.random() * 2 - 1) * (1 - i / n);
    }
    const src = ac.createBufferSource();
    src.buffer = buf;
    const g = ac.createGain();
    g.gain.setValueAtTime(gain, t);
    g.gain.exponentialRampToValueAtTime(0.0001, t + dur);
    const f = ac.createBiquadFilter();
    f.type = 'lowpass';
    f.frequency.value = cutoff;
    src.connect(f).connect(g).connect(ac.destination);
    src.start(t);
    src.stop(t + dur + 0.02);
}

function playKick(intensity) {
    // Scale the whole character by power (intensity 0.2 = dead block .. 1 = full cannon), not just the
    // volume: a hard strike is lower, longer and punchier, so power is audible, not merely louder.
    noise(0.05 + 0.05 * intensity, 0.16 + 0.20 * intensity, 2000 + 1400 * intensity);
    const thud = 230 - 100 * intensity; // 230 Hz light tap → 130 Hz heavy boom
    tone(thud, 0.09 + 0.07 * intensity, 'sine', 0.18 + 0.20 * intensity, thud * 0.5);
}

/** A pass (lane hop or back-pass) — airy and soft, deliberately unlike the kick's low thud. */
function playPass() {
    noise(0.05, 0.09, 1200);
    tone(340, 0.09, 'sine', 0.10, 250);
}

/** A dash (§skill-dash) — a short airy "whoosh" of rushing air: a noise burst through a band-pass
 *  filter whose centre sweeps up then back down, so it reads as air moving past rather than a hit
 *  (unlike the kick's low thud). Roughly the length of the dash glow (DASH_FLASH_MS) and pitched
 *  between the pass and the kick in volume. There's no wire signal for a dash, so — like the cyan
 *  rod glow — it's played locally for the dasher only, from maybeDash on a successful dash. */
function playDash() {
    const ac = ensureAudio();
    if (!ac) {
        return;
    }
    const t = ac.currentTime;
    const dur = 0.22;
    const n = Math.max(1, Math.floor(ac.sampleRate * dur));
    const buf = ac.createBuffer(1, n, ac.sampleRate);
    const data = buf.getChannelData(0);
    for (let i = 0; i < n; i++) {
        data[i] = Math.random() * 2 - 1; // full white noise; the swept band-pass shapes the whoosh
    }
    const src = ac.createBufferSource();
    src.buffer = buf;
    const f = ac.createBiquadFilter();
    f.type = 'bandpass';
    f.Q.value = 0.8;
    f.frequency.setValueAtTime(480, t);
    f.frequency.exponentialRampToValueAtTime(1900, t + dur * 0.45); // rush in
    f.frequency.exponentialRampToValueAtTime(650, t + dur);         // fall away
    const g = ac.createGain();
    g.gain.setValueAtTime(0.0001, t);
    g.gain.exponentialRampToValueAtTime(0.13, t + 0.03); // quick swell, softer than a kick
    g.gain.exponentialRampToValueAtTime(0.0001, t + dur);
    src.connect(f).connect(g).connect(ac.destination);
    src.start(t);
    src.stop(t + dur + 0.02);
}

function playWall(intensity) {
    noise(0.035, 0.12 * intensity, 1700);
    tone(300, 0.05, 'triangle', 0.06 * intensity);
}

function playGoal() {
    tone(420, 0.5, 'triangle', 0.28, 900);
    tone(280, 0.5, 'sine', 0.18, 560);
}

/** Match won (or a viewer's neutral flourish) — a rising major arpeggio over a warm swell. */
function playVictory() {
    [523, 659, 784, 1047].forEach((f, i) => tone(f, 0.24, 'triangle', 0.22, null, i * 0.13)); // C E G C
    tone(262, 0.7, 'sine', 0.12, 392, 0.1);
}

/** Match lost — a short descending, deflating figure. */
function playDefeat() {
    tone(392, 0.32, 'triangle', 0.18, 330, 0);
    tone(311, 0.55, 'triangle', 0.16, 196, 0.26);
}

/** Add screen shake, unless the player asked for reduced motion. */
function addShake(amount) {
    if (!reduceMotion) {
        shake = Math.min(SHAKE_MAX, shake + amount);
    }
}

/** Spawn a radial burst of particles at (x, y) — impact dust / goal confetti. */
function spawnParticles(x, y, count, speed, color, life) {
    for (let i = 0; i < count; i++) {
        const a = Math.random() * Math.PI * 2;
        const sp = speed * (0.4 + Math.random() * 0.6);
        particles.push({
            x, y,
            vx: Math.cos(a) * sp,
            vy: Math.sin(a) * sp,
            born: lastFrameTime,
            life: life * (0.7 + Math.random() * 0.6),
            r: 2 + Math.random() * 2.5,
            color,
        });
    }
    if (particles.length > 240) {
        particles.splice(0, particles.length - 240);
    }
}

/** Impact intensity 0..1 from a ball speed. */
function speedIntensity(vx, vy) {
    return Math.min(1, Math.max(0.2, Math.hypot(vx, vy) / SPEED_REF));
}

/** Advance and expire particles; called once per frame. */
function updateParticles(nowMs, dtSec) {
    for (let i = particles.length - 1; i >= 0; i--) {
        const p = particles[i];
        if (nowMs - p.born >= p.life) {
            particles.splice(i, 1);
            continue;
        }
        p.x += p.vx * dtSec;
        p.y += p.vy * dtSec;
        p.vx *= 0.9;
        p.vy *= 0.9;
    }
}

// ---- Rendering -------------------------------------------------------------------------------

let lastLayoutKey = '';

function resizeCanvas() {
    const parent = canvas.parentElement;
    if (!parent) {
        return;
    }
    const dpr = window.devicePixelRatio || 1;
    const cssWidth = parent.clientWidth;
    lastLayoutKey = cssWidth + ':' + dpr;
    if (cssWidth <= 0) {
        return;
    }
    const cssHeight = cssWidth * (700 + 2 * PAD) / (1200 + 2 * PAD);
    canvas.style.width = cssWidth + 'px';
    canvas.style.height = cssHeight + 'px';
    canvas.width = Math.round(cssWidth * dpr);
    canvas.height = Math.round(cssHeight * dpr);
}

/** ResizeObserver misses some layout changes (and the element can start 0-wide) — cheap
 *  per-frame check keeps the canvas matched to its container. */
function maybeResize() {
    const parent = canvas.parentElement;
    if (parent && parent.clientWidth + ':' + (window.devicePixelRatio || 1) !== lastLayoutKey) {
        resizeCanvas();
    }
}

function frame(nowMs) {
    if (disposed) {
        return;
    }
    rafHandle = requestAnimationFrame(frame);
    const rawFrameMs = lastFrameTime ? nowMs - lastFrameTime : 0;
    const dtSec = Math.min(0.1, (nowMs - lastFrameTime) / 1000 || 0.016);
    lastFrameTime = nowMs;

    // Decay screen shake and age impact particles (Layer 1) — independent of the interp buffer.
    shake *= Math.exp(-SHAKE_DECAY * dtSec);
    if (shake < 0.05) {
        shake = 0;
    }
    updateParticles(nowMs, dtSec);

    // Render frame interval (§12) — only while measuring and visible; skip resume spikes (>1 s) from a
    // backgrounded tab so throttled frames don't masquerade as device jank.
    if (measuring() && !document.hidden && rawFrameMs > 0 && rawFrameMs < 1000) {
        statsFrame.push(rawFrameMs);
    }

    maybeMeasure(nowMs);
    maybeResize();
    if (!config || canvas.width === 0) {
        return;
    }
    const world = sampleWorld(nowMs);
    if (!world) {
        return;
    }
    if (measuring()) {
        sampledFrames++;
        if (world.extrapolated) {
            extrapFrames++;
        }
    }
    const rods = predictOwnRods(world.rods, dtSec, world.renderTime);
    draw(world.ball, rods, world.snap, nowMs, world.renderTime);
}

function draw(ball, rodOffsets, snap, nowMs, renderTime) {
    const W = config.width;
    const H = config.height;

    // Ball teleported (kickoff, reset, rematch) — drop the trail and roll baseline so nothing
    // streaks or spins across the discontinuity.
    if (lastResetCounter !== snap.r) {
        lastResetCounter = snap.r;
        trail = [];
        lastBallX = lastBallY = null;
    }

    // Lane pass: a trapped ball hops to an adjacent figure on the same rod. It's carried, so it reports
    // zero velocity and wouldn't trail like a shot — detect the figure change (same rod, still trapped)
    // and briefly enable the trail so a pass shows the same motion streak. Ordinary dribbling keeps the
    // same figure, so it stays untrailed.
    if (snap.tr >= 0 && snap.tr === seenTrapRod && snap.tf !== seenTrapFig) {
        laneTrailUntil = nowMs + LANE_PASS_TRAIL_MS;
    }
    seenTrapRod = snap.tr;
    seenTrapFig = snap.tf;

    const scale = canvas.width / (W + 2 * PAD);
    // Screen shake: jitter the whole playfield by a small, fast-decaying offset on impacts.
    const shx = shake > 0 ? (Math.random() * 2 - 1) * shake : 0;
    const shy = shake > 0 ? (Math.random() * 2 - 1) * shake : 0;
    ctx.setTransform(scale, 0, 0, scale, (PAD + shx) * scale, (PAD + shy) * scale);
    ctx.clearRect(-PAD - Math.abs(shx), -PAD - Math.abs(shy), W + 2 * PAD + 2 * Math.abs(shx), H + 2 * PAD + 2 * Math.abs(shy));

    const mouthTop = (H - config.goalMouth) / 2;
    const mouthBottom = (H + config.goalMouth) / 2;

    // Frame + felt.
    ctx.fillStyle = '#5d4037';
    roundRect(-PAD, -PAD, W + 2 * PAD, H + 2 * PAD, 18);
    ctx.fill();
    ctx.fillStyle = '#1e8449';
    ctx.fillRect(-6, -6, W + 12, H + 12);

    // Goal pockets behind the goal lines.
    ctx.fillStyle = '#14532d';
    ctx.fillRect(-PAD + 8, mouthTop, PAD - 8, config.goalMouth);
    ctx.fillRect(W, mouthTop, PAD - 8, config.goalMouth);

    // Markings.
    ctx.strokeStyle = 'rgba(255,255,255,0.55)';
    ctx.lineWidth = 3;
    ctx.strokeRect(0, 0, W, H);
    ctx.beginPath();
    ctx.moveTo(W / 2, 0);
    ctx.lineTo(W / 2, H);
    ctx.stroke();
    ctx.beginPath();
    ctx.arc(W / 2, H / 2, 80, 0, Math.PI * 2);
    ctx.stroke();
    // Goal mouths.
    ctx.strokeStyle = 'rgba(255,255,255,0.9)';
    ctx.lineWidth = 5;
    for (const x of [0, W]) {
        ctx.beginPath();
        ctx.moveTo(x, mouthTop);
        ctx.lineTo(x, mouthBottom);
        ctx.stroke();
    }

    // Rods and figures. While you hold catch, all your rods are "armed" (they'll trap the ball) and
    // glow green — immediate feedback that you're in catch mode and where the ball can be caught.
    const ownRodSet = new Set(myRods ? myRods.hands.flat() : []);
    const armedRodSet = new Set(myRods && catchKeys.size > 0 ? myRods.hands.flat() : []);
    // Brief cyan glow on your own rods right after a dash (§skill-dash) — confirms the lunge fired.
    const dashGlow = nowMs < dashFlashUntil ? (dashFlashUntil - nowMs) / DASH_FLASH_MS : 0;
    // Lifted rods (§skill-lift): authoritative for everyone (opponents too) via the snapshot bitmask,
    // plus optimistic own-rod state so your lift shows the instant you press it, before the round-trip.
    const liftedSet = new Set();
    for (let i = 0; i < config.rods.length; i++) {
        if (snap.lf & (1 << i)) liftedSet.add(i);
    }
    if (myRods) {
        for (const h of [0, 1]) {
            if (liftHeld[h]) for (const r of myRods.hands[h]) if (r !== snap.tr) liftedSet.add(r);
        }
    }
    const liftMaxMs = (config.liftSlamMaxCharge || 0.8) * 1000;
    for (let i = 0; i < config.rods.length; i++) {
        const rod = config.rods[i];
        const own = ownRodSet.has(i);
        const armed = armedRodSet.has(i);
        const lifted = liftedSet.has(i);
        // Local drop-slam charge readout (own rods only) — grows from the press, mirrors the server ramp.
        const liftHand = lifted && own && myRods ? myRods.hands.findIndex(h => h.includes(i)) : -1;
        const liftFrac = liftHand >= 0 ? Math.min(1, (nowMs - liftStart[liftHand]) / liftMaxMs) : 0;

        // Charging drop-slam (§skill-lift): the rod trembles harder as it powers up — a coiled-spring
        // tell on top of the brightening gold aura. Isolated in save/restore so only this rod jitters.
        ctx.save();
        if (liftFrac > 0) {
            const amp = liftFrac * LIFT_SHAKE_PX;
            ctx.translate((Math.random() * 2 - 1) * amp, (Math.random() * 2 - 1) * amp);
        }

        if (armed) {
            ctx.shadowColor = 'rgba(80,255,130,0.9)';
            ctx.shadowBlur = 14;
        } else if (lifted && own) {
            // Gold "charging slam" aura on the bar, brightening as the drop-slam powers up.
            ctx.shadowColor = `rgba(255,196,80,${0.4 + 0.5 * liftFrac})`;
            ctx.shadowBlur = 10 + 18 * liftFrac;
        } else if (own && dashGlow > 0) {
            ctx.shadowColor = `rgba(120,200,255,${0.9 * dashGlow})`;
            ctx.shadowBlur = 22 * dashGlow;
        }
        ctx.strokeStyle = armed ? 'rgba(120,255,150,0.98)' : (own ? 'rgba(233,236,239,0.95)' : 'rgba(173,181,189,0.6)');
        ctx.lineWidth = armed ? 8 : (own ? 7 : 5);
        ctx.beginPath();
        ctx.moveTo(rod.x, -PAD + 6);
        ctx.lineTo(rod.x, H + PAD - 6);
        ctx.stroke();
        ctx.shadowBlur = 0;

        // Rod end-stop collars: every rod's figures are inset from the walls (by WallMargin, or more
        // for the short-travel goalie), leaving room for a pair of collars that live on the bar and
        // slide WITH it, like real foosball rod hardware — spaced so each meets its side wall exactly
        // at the travel limit. So a rod reads as a physical bar that stops when a collar hits the
        // wall, not a man halting in open space. yBase > 0 for every inset rod (all of them).
        if (rod.yBase > 0.5) {
            const off = rodOffsets[i];
            const topFigY = rod.yBase + off * rod.travel;               // topmost figure centre
            const botFigY = topFigY + (rod.figures - 1) * rod.spacing;  // bottommost figure centre
            const botGap = H - rod.yBase - rod.travel - (rod.figures - 1) * rod.spacing;
            drawTravelStop(rod.x, topFigY - rod.yBase);                 // → y=0 at the up limit
            drawTravelStop(rod.x, botFigY + botGap);                    // → y=H at the down limit
        }

        if (lifted) {
            ctx.globalAlpha = LIFT_FIGURE_ALPHA; // men rotated up out of the plane — drawn faint (§skill-lift)
        }
        for (let f = 0; f < rod.figures; f++) {
            const y = rod.yBase + rodOffsets[i] * rod.travel + f * rod.spacing;
            drawFigure(rod.x, y, rod.side, own, footValue(i, f, renderTime), rod.radius);
        }
        ctx.globalAlpha = 1;
        ctx.restore();

        // Drop-slam strength readout (§skill-lift): a small gold→red meter above your own charging rod,
        // steady (drawn after restore, so it doesn't tremble with the bar).
        if (liftFrac > 0) {
            drawLiftGauge(rod.x, liftFrac);
        }
    }

    // Anti-stall dismissal warning (§2.5): a dashed ring that spins, tightens and reddens around a ball
    // about to be re-centered for inactivity, so the reset never surprises anyone. snap.dm 0..1 = how
    // imminent — 0 when not (it also can't coincide with a trap, so this never fights the hold ring).
    if (snap.dm > 0) {
        const r = config.ballRadius + 6 + 18 * (1 - snap.dm);
        ctx.beginPath();
        ctx.arc(ball[0], ball[1], r, 0, Math.PI * 2);
        ctx.strokeStyle = `rgba(255,${Math.round(150 * (1 - snap.dm))},60,${0.5 + 0.45 * snap.dm})`;
        ctx.lineWidth = 2.5;
        ctx.setLineDash([6, 5]);
        ctx.lineDashOffset = -nowMs / 45;
        ctx.stroke();
        ctx.setLineDash([]);
        ctx.lineDashOffset = 0;
    }

    // Trap hold timer: a ring that's full when the ball is caught and drains over the hold window
    // (refills on each pass), turning red as it's about to auto-fire. snap.ch = fraction remaining.
    if (snap.tr >= 0 && snap.ch > 0) {
        const side = config.rods[snap.tr].side;
        ctx.beginPath();
        ctx.arc(ball[0], ball[1], config.ballRadius + 9, -Math.PI / 2, -Math.PI / 2 + snap.ch * Math.PI * 2);
        ctx.strokeStyle = snap.ch <= 0.25 ? '#ff5252' : SIDE_COLORS[side];
        ctx.lineWidth = 4;
        ctx.lineCap = 'round';
        ctx.stroke();
        ctx.lineCap = 'butt';
    }

    // Goalie shot-power meter: a second, outer ring that FILLS (green → red) as the caught shot
    // charges from regular strength up to the near-max cannon (§skill). Goalie only — a 1-man rod;
    // the outfield charge is too quick to read as a meter. snap.pw = power fraction 0..1.
    if (snap.tr >= 0 && config.rods[snap.tr] && config.rods[snap.tr].figures === 1) {
        const r = config.ballRadius + 15;
        ctx.beginPath(); // faint full-circle track
        ctx.arc(ball[0], ball[1], r, 0, Math.PI * 2);
        ctx.strokeStyle = 'rgba(255,255,255,0.18)';
        ctx.lineWidth = 3;
        ctx.stroke();
        if (snap.pw > 0) {
            ctx.beginPath();
            ctx.arc(ball[0], ball[1], r, -Math.PI / 2, -Math.PI / 2 + snap.pw * Math.PI * 2);
            ctx.strokeStyle = `hsl(${(1 - snap.pw) * 120}, 90%, 55%)`; // 120°green (weak) → 0°red (max)
            ctx.lineWidth = 3;
            ctx.lineCap = 'round';
            ctx.stroke();
            ctx.lineCap = 'butt';
        }

        // Aim arrow (§skill-aim): a trapped goalie's charged shot fires dead straight along this. Drawn
        // for everyone so opponents can read the shot and slide to cover during the wind-up — the visible
        // aim is the counterplay to a precise cannon. Points straight ahead until the keeper swings it
        // with ↑/↓ (once SPACE is held); grows and turns green → red as power builds. snap.am = aim angle
        // (rad off straight), snap.pw = power fraction, dirX = the side's forward (attacking) direction.
        const side = config.rods[snap.tr].side;
        const dirX = side === 0 ? 1 : -1;
        const aim = snap.am || 0;
        const ux = Math.cos(aim) * dirX;
        const uy = Math.sin(aim);
        const len = config.ballRadius + 30 + (snap.pw || 0) * 85;
        const tipX = ball[0] + ux * len;
        const tipY = ball[1] + uy * len;
        ctx.strokeStyle = ctx.fillStyle = snap.pw > 0 ? `hsl(${(1 - snap.pw) * 120}, 90%, 55%)` : SIDE_COLORS[side];
        ctx.globalAlpha = snap.pw > 0 ? 0.95 : 0.5;
        ctx.lineWidth = 3;
        ctx.lineCap = 'round';
        ctx.beginPath();
        ctx.moveTo(ball[0] + ux * (config.ballRadius + 4), ball[1] + uy * (config.ballRadius + 4));
        ctx.lineTo(tipX, tipY);
        ctx.stroke();
        ctx.lineCap = 'butt';
        const ah = 11, aw = 7, px = -uy, py = ux; // arrowhead: back off along the shaft, spread perpendicular
        ctx.beginPath();
        ctx.moveTo(tipX, tipY);
        ctx.lineTo(tipX - ux * ah + px * aw, tipY - uy * ah + py * aw);
        ctx.lineTo(tipX - ux * ah - px * aw, tipY - uy * ah - py * aw);
        ctx.closePath();
        ctx.fill();
        ctx.globalAlpha = 1;
    }

    // Motion trail: a fading streak behind a fast-moving ball (Layer 1). Always record + prune; render
    // above the speed threshold so a slow roll doesn't smear, or briefly after a lane pass (the carried
    // ball has zero velocity but is visibly sliding to the next man).
    const ballSpeed = Math.hypot(snap.v[0], snap.v[1]);
    const lanePassing = nowMs < laneTrailUntil;
    trail.push({ x: ball[0], y: ball[1], t: nowMs });
    while (trail.length && nowMs - trail[0].t > TRAIL_MS) {
        trail.shift();
    }
    if ((ballSpeed > TRAIL_MIN_SPEED || lanePassing) && trail.length > 1) {
        // A carried lane-pass ball reports no speed, so give it a fixed, gentle streak.
        const strength = ballSpeed > TRAIL_MIN_SPEED ? Math.min(1, (ballSpeed - TRAIL_MIN_SPEED) / 600 + 0.35) : 0.5;
        for (let i = 0; i < trail.length - 1; i++) {
            const age = (nowMs - trail[i].t) / TRAIL_MS; // 0 = freshest end of the tail
            ctx.beginPath();
            ctx.arc(trail[i].x, trail[i].y, config.ballRadius * (1 - age) * 0.92, 0, Math.PI * 2);
            ctx.fillStyle = `rgba(248,249,250,${((1 - age) * 0.26 * strength).toFixed(3)})`;
            ctx.fill();
        }
    }

    // Ball.
    ctx.beginPath();
    ctx.arc(ball[0], ball[1], config.ballRadius, 0, Math.PI * 2);
    ctx.fillStyle = '#f8f9fa';
    ctx.shadowColor = 'rgba(0,0,0,0.4)';
    ctx.shadowBlur = 6;
    ctx.fill();
    ctx.shadowBlur = 0;
    ctx.strokeStyle = 'rgba(0,0,0,0.25)';
    ctx.lineWidth = 1.5;
    ctx.stroke();

    // Rolling surface markers: accumulate the ball's rotation from how far it just travelled (roll)
    // plus its spin (english), then draw front-hemisphere pips so it visibly rolls and curves.
    if (lastBallX !== null) {
        ballAngY += (ball[0] - lastBallX) / config.ballRadius;
        ballAngX -= (ball[1] - lastBallY) / config.ballRadius;
    }
    lastBallX = ball[0];
    lastBallY = ball[1];
    ballAngZ += (snap.sp || 0) * 0.05;
    for (const m of BALL_MARKERS) {
        const p = rot3(m, ballAngX, ballAngY, ballAngZ);
        if (p[2] < -0.15) {
            continue; // back of the ball — hidden
        }
        const front = (p[2] + 1) / 2;
        ctx.beginPath();
        ctx.arc(ball[0] + p[0] * config.ballRadius * 0.82, ball[1] + p[1] * config.ballRadius * 0.82,
            config.ballRadius * 0.2 * (0.55 + 0.45 * front), 0, Math.PI * 2);
        ctx.fillStyle = `rgba(70,80,95,${(0.22 + 0.5 * front).toFixed(3)})`;
        ctx.fill();
    }

    // Impact particles (kick dust, wall dust, goal confetti) over the ball.
    for (const pt of particles) {
        const life = Math.min(1, Math.max(0, 1 - (nowMs - pt.born) / pt.life));
        ctx.globalAlpha = life;
        ctx.beginPath();
        ctx.arc(pt.x, pt.y, pt.r * (0.4 + 0.6 * life), 0, Math.PI * 2);
        ctx.fillStyle = pt.color;
        ctx.fill();
    }
    ctx.globalAlpha = 1;

    // Score.
    ctx.fillStyle = 'rgba(255,255,255,0.85)';
    ctx.font = 'bold 40px system-ui, sans-serif';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'top';
    ctx.fillText(`${snap.s[0]} : ${snap.s[1]}`, W / 2, 12);

    // Match clock, under the score.
    ctx.fillStyle = 'rgba(255,255,255,0.65)';
    ctx.font = 'bold 20px system-ui, sans-serif';
    ctx.fillText(formatClock(snap.mt), W / 2, 56);

    // HUD, top-left. Dash-cooldown pip first (seated players): a lightning bolt that fills from dim to
    // bright cyan as the cooldown recharges — instant read on whether the dash is ready (§skill-dash).
    let hudY = 10;
    if (myRods && config && config.dashCooldownSeconds) {
        const remaining = Math.max(0, dashReadyAt - nowMs);
        const frac = 1 - Math.min(1, remaining / (config.dashCooldownSeconds * 1000)); // 0 just-dashed → 1 ready
        ctx.save();
        ctx.textAlign = 'left';
        ctx.textBaseline = 'top';
        ctx.font = 'bold 22px system-ui, sans-serif';
        ctx.shadowColor = 'rgba(0,0,0,0.6)';
        ctx.shadowBlur = 3;
        ctx.globalAlpha = 0.3 + 0.7 * frac;
        ctx.fillStyle = frac >= 1 ? '#4dd2ff' : '#9ecbff';
        ctx.fillText('⚡', 12, hudY);
        ctx.restore();
        hudY += 30;
    }

    // Ping readout (player option §12) — colored by quality. This is connection RTT: your own rods are
    // client-predicted, so it's not your control lag, but it's the best "is my link ok" signal.
    if (playerOptions.showPing && displayPing !== null) {
        const ms = Math.round(displayPing);
        ctx.save();
        ctx.fillStyle = ms < 60 ? '#51cf66' : ms < 120 ? '#fcc419' : '#ff6b6b';
        ctx.font = 'bold 22px system-ui, sans-serif';
        ctx.textAlign = 'left';
        ctx.textBaseline = 'top';
        ctx.shadowColor = 'rgba(0,0,0,0.6)';
        ctx.shadowBlur = 3;
        ctx.fillText(`● ${ms} ms`, 10, hudY);
        ctx.restore();
    }

    // GOAL flash (transient, canvas-drawn — §5.1).
    if (flash) {
        const remaining = flash.until - nowMs;
        if (remaining <= 0) {
            flash = null;
        } else {
            ctx.globalAlpha = Math.min(1, remaining / 800);
            ctx.font = 'bold 110px system-ui, sans-serif';
            ctx.textBaseline = 'middle';
            ctx.lineWidth = 10;
            ctx.strokeStyle = flash.color;
            ctx.strokeText(flash.text, W / 2, H / 2);
            ctx.fillStyle = '#fff';
            ctx.fillText(flash.text, W / 2, H / 2);
            ctx.globalAlpha = 1;
        }
    }
}

/** Top-down foosball man: shoulders along the rod, head on top; while kicking, the foot
 *  sweeps out perpendicular to the rod, toward the goal his team attacks. The body scales with
 *  the figure's collision radius so a fatter goalie also looks fatter (honest hitbox). */
function drawFigure(x, y, side, own, foot, radius) {
    const attackDir = side === 0 ? 1 : -1;
    const s = radius / config.figureRadius; // outfield man = 1, the bigger goalie > 1

    if (Math.abs(foot) > 0.05) {
        const fx = x + attackDir * foot * FOOT_EXTENT;
        ctx.beginPath();
        ctx.ellipse(fx, y, 8 * s, 6 * s, 0, 0, Math.PI * 2);
        ctx.fillStyle = SIDE_DARK[side];
        ctx.fill();
        ctx.lineWidth = 1.5;
        ctx.strokeStyle = 'rgba(0,0,0,0.3)';
        ctx.stroke();
    }

    roundRect(x - 10 * s, y - 17 * s, 20 * s, 34 * s, 9 * s);
    ctx.fillStyle = SIDE_COLORS[side];
    ctx.fill();
    ctx.lineWidth = own ? 3 : 2;
    ctx.strokeStyle = own ? '#fff' : 'rgba(0,0,0,0.35)';
    ctx.stroke();

    ctx.beginPath();
    ctx.arc(x, y, 6.5 * s, 0, Math.PI * 2);
    ctx.fillStyle = SIDE_DARK[side];
    ctx.fill();
    ctx.lineWidth = 1;
    ctx.strokeStyle = 'rgba(0,0,0,0.25)';
    ctx.stroke();
}

/** A rubber end-stop collar on a rod — drawn for every rod (all are inset from the walls) and slid
 *  along with the bar (see the rod loop), so it meets the side wall at the travel limit like real
 *  rod hardware. A dark, slightly highlighted bar across the rod. */
function drawTravelStop(x, y) {
    // Understated: every rod carries a pair, so keep them as ambient dark hardware, not focal.
    roundRect(x - 7.5, y - 3.5, 15, 7, 3.5);
    ctx.fillStyle = 'rgba(33,37,41,0.72)';
    ctx.fill();
    ctx.lineWidth = 1;
    ctx.strokeStyle = 'rgba(255,255,255,0.16)';
    ctx.stroke();
}

/** Drop-slam charge meter (§skill-lift): a small pill just above your own charging rod, filling
 *  gold → orange → red as the slam powers up — the strength you'll unleash when you drop the men. */
function drawLiftGauge(x, frac) {
    const w = 26, h = 5, gx = x - w / 2, gy = -PAD + 3;
    roundRect(gx, gy, w, h, 2.5);
    ctx.fillStyle = 'rgba(0,0,0,0.45)';
    ctx.fill();
    ctx.fillStyle = frac < 0.55 ? '#ffd24a' : (frac < 0.85 ? '#ff9f43' : '#ff5252');
    ctx.fillRect(gx, gy, w * frac, h);
}

/** Whole seconds → m:ss. */
function formatClock(totalSeconds) {
    const total = Math.max(0, totalSeconds | 0);
    const sec = total % 60;
    return `${(total / 60) | 0}:${sec < 10 ? '0' : ''}${sec}`;
}

/** Move `value` toward `target` by at most `maxStep` — mirrors GamePhysics.MoveToward. */
function moveToward(value, target, maxStep) {
    return Math.abs(target - value) <= maxStep ? target : value + Math.sign(target - value) * maxStep;
}

/** Rotate a 3-vector by Rx(ax)·Ry(ay)·Rz(az) — used to spin the ball's surface markers. */
function rot3(p, ax, ay, az) {
    let [x, y, z] = p;
    const cx = Math.cos(ax), sx = Math.sin(ax);
    [y, z] = [y * cx - z * sx, y * sx + z * cx];
    const cy = Math.cos(ay), sy = Math.sin(ay);
    [x, z] = [x * cy + z * sy, -x * sy + z * cy];
    const cz = Math.cos(az), sz = Math.sin(az);
    [x, y] = [x * cz - y * sz, x * sz + y * cz];
    return [x, y, z];
}

function roundRect(x, y, w, h, r) {
    ctx.beginPath();
    ctx.moveTo(x + r, y);
    ctx.arcTo(x + w, y, x + w, y + h, r);
    ctx.arcTo(x + w, y + h, x, y + h, r);
    ctx.arcTo(x, y + h, x, y, r);
    ctx.arcTo(x, y, x + w, y, r);
    ctx.closePath();
}

// ---- Connection overlay ------------------------------------------------------------------------

function showOverlay(message, offerReload) {
    hideOverlay();
    const parent = canvas?.parentElement;
    if (!parent) {
        return;
    }
    overlayEl = document.createElement('div');
    overlayEl.className = 'game-connection-overlay';
    const text = document.createElement('div');
    text.textContent = message;
    overlayEl.appendChild(text);
    if (offerReload) {
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'btn btn-light btn-sm mt-2';
        btn.textContent = 'Reload';
        btn.addEventListener('click', () => window.location.reload());
        overlayEl.appendChild(btn);
    }
    parent.appendChild(overlayEl);
}

function hideOverlay() {
    overlayEl?.remove();
    overlayEl = null;
}
