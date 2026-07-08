// Live game canvas client (spec §4.4): a dumb terminal over the game hub — receives
// snapshots + roomState, interpolates, renders with rAF, captures keyboard input and
// predicts the two own rods locally. Zero game rules live here.

const INTERP_DELAY_MS = 125;      // render this far behind server time (§4.4)
const MAX_EXTRAPOLATION_MS = 100; // cap ball extrapolation past the newest snapshot
const NUDGE_RATE = 4;             // /s — correction of predicted own rods by time-aligned error
const PAD = 30;                   // logical padding around the playfield (goal pockets, handles)

// Mirrors SeatMap.cs: [seat][hand] → rod, and the 1v1 pairs [side][hand] → rods (§2.2).
// Hands are screen-relative — hand 0 (W/S) always drives the rod(s) nearer the left edge.
const OWN_RODS = [[0, 1], [3, 5], [6, 7], [2, 4]];
const PAIR_RODS = [[[0, 1], [3, 5]], [[2, 4], [6, 7]]];
const SIDE_COLORS = ['#ffc107', '#0d6efd'];  // A yellow, B blue (§5.1)
const SIDE_DARK = ['#c79100', '#0a53be'];    // head + swinging foot shades
const SWING_MS = 300;                        // kick swing animation length
const FOOT_EXTENT = 24;                      // how far the foot sweeps from the rod

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

// Input & own-rod prediction.
const held = [{ up: false, down: false }, { up: false, down: false }];
const sentDir = [0, 0];
const catchKeys = new Set(); // currently-pressed catch keys (any Shift / alternate)
let sentCatch = false;
let sentSpace = false; // last SPACE held-state sent (down/up edges only)
let myRods = null;      // { hands: [rodIdxs, rodIdxs] } when seated, else null
let predicted = null;   // rodIdx → offset
let predHistory = {};   // rodIdx → [{ t, o }] — past predictions on the server timeline
let lastFrameTime = 0;

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
    myRods = null;
    predicted = null;
    predHistory = {};
    lastLayoutKey = '';
    lastFrameTime = 0;
    for (const hand of [0, 1]) {
        held[hand].up = held[hand].down = false;
        sentDir[hand] = 0;
    }
    catchKeys.clear();
    sentCatch = false;
    sentSpace = false;

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
    connection.onreconnecting(() => showOverlay('Reconnecting…', false));
    connection.onreconnected(async () => {
        // A reconnected connection is brand new to the server — group membership does not
        // survive it; re-join and re-send held-key state (§3.5). Score and kick baselines
        // reset too, so a goal scored or kick made while away doesn't flash/replay on rejoin.
        hideOverlay();
        snapshots = [];
        clockOffset = null;
        lastScore = null;
        lastKickTick = -1;
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
    const serverTime = snap.t * (1000 / config.tickRate);
    const sample = serverTime - performance.now();
    clockOffset = clockOffset === null ? sample : clockOffset + (sample - clockOffset) * 0.1;

    snapshots.push(snap);
    if (snapshots.length > 60) {
        snapshots.splice(0, snapshots.length - 60);
    }

    // New auto-kick → queue the figure's swing at its exact server time. The first snapshot
    // (join/rejoin) only sets the baseline so an old kick doesn't replay.
    if (snap.k) {
        if (lastKickTick < 0) {
            lastKickTick = snap.kt;
        } else if (snap.kt > lastKickTick) {
            lastKickTick = snap.kt;
            if (snap.k[0] >= 0) {
                swings.push({ rod: snap.k[0], fig: snap.k[1], tMs: snap.kt * (1000 / config.tickRate) });
                if (swings.length > 12) {
                    swings.shift();
                }
            }
        }
    }

    // GOAL flash on a score increment; a decrease is a reset/rematch — no flash (§5.1).
    if (lastScore) {
        if (snap.s[0] > lastScore[0]) {
            flash = { text: 'GOAL!', color: SIDE_COLORS[0], until: performance.now() + 1200 };
        } else if (snap.s[1] > lastScore[1]) {
            flash = { text: 'GOAL!', color: SIDE_COLORS[1], until: performance.now() + 1200 };
        }
    }
    lastScore = snap.s;
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
        predicted = null;
        predHistory = {};
        return;
    }
    const side = mySeat.seat <= 1 ? 0 : 1;
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

function onKeyDown(e) {
    if (!myRods || isTyping(e)) {
        return;
    }
    if (e.code === 'Space') {
        e.preventDefault(); // don't scroll, and don't trigger a focused button
        // Held state: press starts the goalie's charge (and the outfield pass); release launches the
        // goalie shot. Edges only — key-repeat is ignored.
        if (!e.repeat && !sentSpace) {
            sentSpace = true;
            connection?.send('Space', true).catch(() => { });
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

function releaseAllKeys() {
    for (const hand of [0, 1]) {
        held[hand].up = held[hand].down = false;
        syncHand(hand); // a backgrounded tab must not leave a rod gliding into the wall
    }
    catchKeys.clear();
    syncCatch(); // …nor holding a ball trapped
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
    if (sentSpace) {
        connection?.send('Space', true).catch(() => { });
    }
}

// ---- Interpolation & prediction ---------------------------------------------------------------

/** Ball position/velocity and rod offsets at render time (~125 ms behind server). */
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
    // Past the newest snapshot — extrapolate the ball briefly from its velocity (§4.3).
    const newest = snapshots[snapshots.length - 1];
    const dt = Math.min(Math.max(0, renderTime - newest.t * tickMs), MAX_EXTRAPOLATION_MS) / 1000;
    return {
        ball: [newest.b[0] + newest.v[0] * dt, newest.b[1] + newest.v[1] * dt],
        rods: newest.o,
        snap: newest,
        renderTime,
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
    for (const hand of [0, 1]) {
        for (const rodIdx of myRods.hands[hand]) {
            const rod = config.rods[rodIdx];
            let p = predicted[rodIdx] ?? rods[rodIdx];
            p += sentDir[hand] * (config.rodSpeed / rod.travel) * dtSec;
            p = Math.min(1, Math.max(0, p));
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
    const dtSec = Math.min(0.1, (nowMs - lastFrameTime) / 1000 || 0.016);
    lastFrameTime = nowMs;

    maybeResize();
    if (!config || canvas.width === 0) {
        return;
    }
    const world = sampleWorld(nowMs);
    if (!world) {
        return;
    }
    const rods = predictOwnRods(world.rods, dtSec, world.renderTime);
    draw(world.ball, rods, world.snap, nowMs, world.renderTime);
}

function draw(ball, rodOffsets, snap, nowMs, renderTime) {
    const W = config.width;
    const H = config.height;
    const scale = canvas.width / (W + 2 * PAD);
    ctx.setTransform(scale, 0, 0, scale, PAD * scale, PAD * scale);
    ctx.clearRect(-PAD, -PAD, W + 2 * PAD, H + 2 * PAD);

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
    for (let i = 0; i < config.rods.length; i++) {
        const rod = config.rods[i];
        const own = ownRodSet.has(i);
        const armed = armedRodSet.has(i);

        if (armed) {
            ctx.shadowColor = 'rgba(80,255,130,0.9)';
            ctx.shadowBlur = 14;
        }
        ctx.strokeStyle = armed ? 'rgba(120,255,150,0.98)' : (own ? 'rgba(233,236,239,0.95)' : 'rgba(173,181,189,0.6)');
        ctx.lineWidth = armed ? 8 : (own ? 7 : 5);
        ctx.beginPath();
        ctx.moveTo(rod.x, -PAD + 6);
        ctx.lineTo(rod.x, H + PAD - 6);
        ctx.stroke();
        ctx.shadowBlur = 0;

        for (let f = 0; f < rod.figures; f++) {
            const y = rod.yBase + rodOffsets[i] * rod.travel + f * rod.spacing;
            drawFigure(rod.x, y, rod.side, own, footValue(i, f, renderTime), rod.radius);
        }
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

/** Whole seconds → m:ss. */
function formatClock(totalSeconds) {
    const total = Math.max(0, totalSeconds | 0);
    const sec = total % 60;
    return `${(total / 60) | 0}:${sec < 10 ? '0' : ''}${sec}`;
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
