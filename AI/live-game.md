# Live Game — Online Foosball Mini-Game

**Status**: Implemented (2026-07-06) as specced; §10 lists the small details settled during
implementation. Code map: `Game/Core/` (constants, DTOs, `SimState`, `GamePhysics`, `SeatMap` —
no server dependencies), `Game/` (`GameRoom`, `GameRoomManager`, `GameHub`),
`wwwroot/js/game.js` + vendored `wwwroot/lib/signalr/` (@microsoft/signalr 9.0.6),
`Components/Pages/Team/LiveGame.razor`.

## 1. Description

A fun real-time multiplayer mini-game: play foosball in the browser against teammates. Up to
4 players control the rods of a virtual table; everyone else can watch live. Purely a
team-level social feature — it does not affect ELO, matches, or seasons (v1).

**Design principle**: same PoC spirit as the rest of the app — server-authoritative,
in-memory, no persistence, no over-engineering. A server restart simply ends any running game.

**Future-proofing note**: v1 is team-scoped (one game per team), but rooms are modeled with
their own identity (`RoomId`) and a pluggable join-authorization step, so a later version can
make games joinable across teams or via share links without restructuring.

---

## 2. Game Rules

### 2.1 Table

Standard foosball layout, top-down view, horizontal orientation (goals left/right).
8 rods, figure counts follow a standard table:

```
        Rod:   1     2     3     4     5     6     7     8
       Team:   A     A     B     A     B     A     B     B
    Figures:   1     2     3     5     5     3     2     1
       Role:  GK    DEF   ATK   MID   MID   ATK   DEF   GK
 ┌──────────────────────────────────────────────────────────┐
 │         |     |     |     |     |     |     |     |      │
 ═   A     |     o     o     o     o     o     o     |    B ═
 ═  goal   o     o     o     o     o     o     o     o goal ═
 │         |     |     o     o     o     o     |     |      │
 └──────────────────────────────────────────────────────────┘
```

- (The diagram is schematic — 5-figure rods are drawn with 3 figures; the header row has
  the real counts.)
- Rods are evenly spaced along the table length (x-positions in §4.6). Figures on a rod
  are evenly spaced; each rod slides vertically within its travel range
  (travel = table height − span between outermost figure centers).
- **Default figure spacing = `tableHeight / figureCount`.** This is the unique choice where
  every rod sweeps the full table height with zero overlap and zero dead lanes
  (travel = `H/n`): the 5-figure MID travels 140 units, the 3-figure ATK 233, the 2-figure
  DEF 350, and the single goalie sweeps the whole height — no dead corners it can't reach
  (real 1-goalie tables solve those with sloped corners; we have none, so full travel it is).
- Goal mouth: centered on each end wall, ~1/3 of table height.

### 2.2 Seats and rod ownership

4 seats, mapped like real doubles foosball — each seat owns two rods:

| Seat | Side | Rods |
|------|------|------|
| A-Defense | A | 1 (GK) + 2 (DEF) |
| A-Attack | A | 4 (MID) + 6 (ATK) |
| B-Defense | B | 8 (GK) + 7 (DEF) |
| B-Attack | B | 5 (MID) + 3 (ATK) |

**Controls — keyboard, two hands like real foosball.** Each seated player controls their
two rods **independently**. Hands are **screen-relative** (settled after the first hands-on
test — goal-relative felt wrong for side B, whose goal is on the right while `W`/`S` sits
on the left of the keyboard):

- `W` / `S` — the seat's rod **nearer the left edge of the screen** (left hand).
- `↑` / `↓` — the rod **nearer the right edge of the screen** (right hand).

| Seat | `W`/`S` rod | `↑`/`↓` rod |
|------|-------------|-------------|
| A-Defense | 1 (GK) | 2 (DEF) |
| A-Attack | 4 (MID) | 6 (ATK) |
| B-Defense | 7 (DEF) | 8 (GK) |
| B-Attack | 3 (ATK) | 5 (MID) |

- Holding a key moves the rod at constant **rod speed**; releasing stops it. Rod positions
  are integrated **server-side** from held-key state, which means rod velocity is always
  known on the server — this is what makes the rod-momentum kick (§2.3) nearly free.
- Input messages are sent only on key state **changes** (down/up), not per-frame — a few
  messages per second per player.
- The client sends **hand-level** input (`left` = `W`/`S`, `right` = `↑`/`↓`), not rod
  numbers; the server maps hand → rod(s) from the sender's seat. Clients never reference
  rods directly, so ownership can't be spoofed and the 1v1 pairing below needs zero client
  logic (the same two hands just map to two rods each).
- These bindings are v1 defaults; revisit after the first playtest (remapping UI is a
  future nicety, not v1).
- **1v1 / short-handed play**: the lone player on a side controls all four rods in two
  role pairs (GK + DEF together, MID + ATK together); each hand drives the pair nearer its
  side of the screen — side A: `W`/`S` = defensive pair; side B: `W`/`S` = offensive pair.
  Same two-hand feel, double the figures. (Alternative considered — auto-assigning keys to
  the rod nearest the ball — rejected as unpredictable; pairs keep it learnable.)
- **Unmanned rods** — with the 1v1 pairing above, every rod on a side with ≥1 seated
  player is controlled, so rods are unmanned only while their whole side is empty (the
  game is then `waiting`, §3.4) or while a disconnected player's seat is grace-held
  (§3.5). Unmanned rods still block and auto-kick (passive walls, zero rod momentum).
  When a side empties entirely its rods glide back to center offset `0.5` at rod speed
  (no teleport); when one of two players leaves a side, their rods do **not** re-center —
  control transfers to the remaining player's pairs. Grace-held rods freeze in place,
  still blocking and auto-kicking like any unmanned rod — they just don't re-center.

### 2.3 Ball and auto-kick

- Ball is a circle with position + velocity, integrated on the server at a fixed timestep.
- Bounces elastically off side walls and end walls (outside the goal mouth) with slight
  damping; mild friction slows the ball over time.
- **Auto-kick**: when the ball comes within contact range of a figure (nearest figure
  wins if two qualify), the figure kicks it automatically **toward the opponent's goal**:
  - Horizontal velocity: fixed kick speed in the attacking direction of the figure's team.
  - Vertical deflection has two components, so shots are aimable:
    1. **Contact offset** — proportional to where on the figure the ball hits
       (`ball.y − figure.y`).
    2. **Rod momentum ("english")** — a fraction (~35%) of the rod's current vertical
       velocity is added to the ball's vertical velocity. Kicking while sliding the rod
       curves the shot. Free to implement: rods are integrated server-side (§2.2), so
       their velocity is already known at contact time.
  - Per-figure cooldown (~200 ms) prevents machine-gun re-kicks; during cooldown the figure
    is a passive collider (ball bounces off it normally).
  - The kicked ball ignores the kicker's collider until the two separate, so a ball kicked
    from behind a figure (e.g. pinned between the GK and its own goal) passes through
    cleanly instead of bouncing straight back off the figure that just kicked it.
- Ball speed is capped at a max.

### 2.4 Scoring

- **Automatic**: a goal is scored when the ball fully crosses the goal line inside the goal
  mouth. Score increments, a short "GOAL" flash is shown, and after a ~1 s pause the ball
  resets to center with a gentle kickoff toward the team that conceded. On any other
  entry into `playing` from a center-parked ball (game start, opponents arriving after a
  waiting spell, rematch) the kickoff direction is random.
- **Lets (optional, per-room)** — two toggles in the lobby's *Match settings* wave off cheap
  goals: instead of scoring, the ball re-parks and the kickoff is redone neutrally. Both are
  scoped to a *fresh* kickoff (a reconnect resume opts out, so a shot already in flight still
  counts). **No quick goals** (default on): goal within `QuickGoalGraceSeconds` (1.5 s) of the
  round going live. **No first-touch goals** (default off): goal off ≤ `FirstTouchMaxContacts`
  (1) uncontrolled figure contacts — 0 = served straight in, 1 = a one-touch finish — for the
  cheap goals that beat the grace window; a deliberately trapped/controlled ball is exempt, so
  genuine trap-and-shoot still scores. Contacts are counted as rising edges (a multi-tick trap
  is one touch), reset each kickoff.
- Game plays **first to 10** (consistent with real matches in the app). On 10, a "game
  over" overlay announces the winners **by name** — e.g. "🏆 Alice & Bob win 10 : 6" (names
  and avatars of the winning seats' occupants, captured at the moment the winning goal
  lands — occupants may leave or swap before the overlay is dismissed) — with a
  **Rematch** button (any seated player; resets score, keeps seats).

### 2.5 Anti-stall

- If ball speed stays below a threshold for ~5 s, or no figure has touched the ball for
  ~15 s, the ball auto-resets to center with a small random velocity.
- Runs only in the `playing` state (not while waiting for opponents or on the game-over
  screen, where the ball is intentionally frozen).
- Manual **Reset ball** button as backup (see controls).

---

## 3. Session Lifecycle & Lobby

### 3.1 One game per team (v1)

At most one live game room exists per team at a time. Room state is in-memory only.

```
  (no room) --Start live game--> Lobby/Playing --last person leaves / idle timeout--> (no room)
```

### 3.2 Starting a game

- **Entry point**: a button next to **New Match** in the `TeamLayout` header —
  `🎮 Live Game` (label when no room exists: "Start live game").
- Clicking it creates the room, seats the user in the first free seat, and navigates to
  `/team/{TeamCode}/game`. (The creator's seat starts in the not-yet-connected grace
  state until the game page's JS client joins the room — one liveness rule, §3.5.)
- Visiting `/team/{TeamCode}/game` directly when no room exists shows an empty state with a
  "Start live game" button (same action). If the room is destroyed while someone is on the
  page (End game, idle cleanup), the page shows a "game ended" notice with the same button.

### 3.3 Team-wide visibility

While a room exists, the rest of the team UI reflects it **live** (same pattern as
`PresenceTracker` — singleton raising `Changed`, circuits subscribe):

- The header button turns into a pulsing badge: `🔴 Live game · 2/4 · Watch/Join`.
- The dashboard shows a banner card: seated players (avatars), seats free, viewer count,
  and a **Join** / **Watch** button.

### 3.4 Joining

- **As player**: anyone in the team can take any free seat (max 4 seated). Seat picker
  shows the 4 seats with occupants; free seats are clickable.
- **As viewer**: anyone can join as viewer **at any time** — before the game is full, while
  it's running, whenever. Viewers see the same live table, plus the seat picker to hop in
  if a seat is free.
- **Seat swap**: a seated player can move to any **empty** seat at any time (e.g. switch
  from A-Attack to B-Defense to rebalance). Occupied seats cannot be taken over.
- **One seat per user**; a user's seat follows their account, not their tab/device. The
  same account in multiple tabs counts once in the viewer list (dedupe by user); a seated
  user's input is accepted from any of their connections, last write wins.
- **Ball in play** requires at least 1 **connected** seated player on **each side** (a
  grace-held seat keeps its owner but doesn't count as connected, §3.5); otherwise the
  game drops to `waiting` with the ball frozen and the **score kept**. Two flavors of the
  same state:
  - **Nobody seated on a side** → ball parks at center, "Waiting for opponents…".
  - **Side seated but disconnected** (grace hold) → ball freezes **in place**, velocity
    stored, and resumes ~1 s after the player reconnects — an automatic pause ("Waiting
    for Bob to reconnect…"). If the grace expires, the seat frees and the ball re-parks
    at center.

### 3.5 Leaving & disconnects

- Leaving the page (or pressing **Leave seat**) frees the seat immediately → becomes a viewer or exits.
- On an unexpected disconnect, the seat is held for a **30 s grace period** — their rods
  freeze in place, and if that leaves the side with no connected player the ball pauses
  too (§3.4); if the player reconnects they resume, otherwise the seat frees up.
- **One liveness rule**: a seat whose user has **zero live hub connections in the room**
  is in the grace state — however it got there. The grace timer starts whenever the
  seat's connection count hits zero, which also covers seats taken *before* the JS
  client connects (the room creator is seated mid-navigation, §3.2; a viewer can grab a
  seat the instant the page renders). No special cases: a connection arrives within
  30 s and the seat activates; none does, and it frees.
- **How the server tells leave from drop**: `OnDisconnectedAsync(Exception?)` —
  documented behavior: `connection.stop()` (which `game.dispose()` calls on page
  navigation/teardown) yields a `null` exception; error disconnects (network loss,
  crash, ping timeout) yield non-null. Tab close is undocumented — browsers usually
  send a clean WebSocket close (→ treated as leave), and if one lands in the error
  bucket instead the cost is a harmless 30 s grace-hold. The seat reacts only when the
  user's **last** connection in the room goes: graceful → freed immediately, abnormal →
  grace period. Closing one of several tabs changes nothing.
- **Timings stack**: a hard drop is only *detected* after up to `ClientTimeoutInterval`
  (30 s default), so a vanished opponent can pause the ball for up to ~60 s total before
  the seat frees. If playtests find that annoying, shorten detection per-hub via
  `AddHubOptions<GameHub>` (`KeepAliveInterval`/`ClientTimeoutInterval`) rather than
  cutting the grace period.
- The JS client uses automatic reconnect (custom retry schedule, §4.4) and re-invokes
  `JoinRoom` in `onreconnected` (a reconnected connection is brand new to the server —
  SignalR group membership does not survive it); since the seat is bound to the user,
  reconnecting within grace resumes it automatically.
- Room is destroyed when it has had no seated players **and** no viewers for ~2 minutes,
  or immediately via **End game** (any seated player; confirmation modal). A lone AFK tab
  parked on the game page does keep the room alive indefinitely — accepted for the PoC
  (anyone can take a seat and End game; a restart clears everything).
- Server restart destroys all rooms (documented, acceptable for PoC).
- (Separate concern: a Blazor **circuit** blip shows the app's standard reconnect dialog
  over the page while the game hub may still be live underneath. Circuit and hub share
  the same network, so in practice they drop and recover together — no coordination in v1.)

### 3.6 In-game controls

Kept deliberately minimal:

| Control | Who | Behavior |
|---------|-----|----------|
| Reset score | Any seated player | Sets score 0:0 (confirmation not needed — it's casual) |
| Reset ball | Any seated player | Re-centers ball with small random velocity (for freezes); no-op unless `playing` |
| Swap seat | Any seated player | Move to an empty seat |
| Take seat | Any viewer | Sit in an empty seat |
| Leave seat | Any seated player | Become viewer |
| End game | Any seated player | Destroys room for everyone (confirm modal) |
| Rematch | Any seated player | After game over: reset score, keep seats |

### 3.7 Identity & display

- Seat occupant = the logged-in **user** (`AppUser`). Display name/avatar comes from their
  **claimed Player** in this team (`Player.UserId`) when one exists, else the user's
  display name with a generic avatar. (In practice everyone has one — `TeamLayout` redirects
  members without a claimed Player to `/team/{code}/claim` before they can reach any team
  page; the fallback is defensive.)
- Viewers are listed **with names and avatars** (same resolution rule as seats), plus a
  total count in the header (`👁 3`).

---

## 4. Architecture

### 4.1 The key constraint

Blazor Server's render tree must **not** be in the frame loop. Re-rendering a component
30×/s would serialize render-tree diffs over the circuit per frame — janky and expensive.
The circuit handles lobby/UI state (seats, score, buttons); the moving game state flows
through a **dedicated SignalR hub straight to a JS canvas renderer**, bypassing Blazor
rendering entirely.

```
┌─ Server ──────────────────────────────────────┐      ┌─ Browser ───────────────────────┐
│ GameRoomManager (singleton)                   │      │ /team/{code}/game (Blazor page) │
│  └─ GameRoom (per team)                       │      │  ├─ lobby UI, seats, buttons    │
│      ├─ sim loop: PeriodicTimer @ 60 Hz       │      │  │   (normal Blazor circuit)    │
│      └─ snapshot broadcast @ 20 Hz ────────────────▶ │  └─ <canvas> + game.js          │
│ GameHub (/hubs/game)  ◀───────────────────────────── │      SignalR JS client:         │
│  cookie-auth, membership check, input intake  │      │      recv snapshots, interp,    │
│                                               │      │      rAF render, send input     │
└───────────────────────────────────────────────┘      └─────────────────────────────────┘
```

### 4.2 Server components

| Component | Lifetime | Responsibility |
|-----------|----------|----------------|
| `GameRoomManager` | Singleton | Rooms keyed by `RoomId`, plus a TeamId → RoomId index enforcing one-per-team (a v1 policy, not a structural limit — §7); create/find/destroy; raises `Changed` for lobby UI (mirrors `PresenceTracker`) |
| `GameRoom` | Per active game | Seats, viewers, score, sim state; owns its tick loop (`PeriodicTimer`); exposes a **thread-safe API** for all mutations — input intake, `TakeSeat`, `SwapSeat`, `LeaveSeat`, `ResetBall`, `ResetScore`, `EndGame`, `Rematch` |
| `GamePhysics` | Static/pure | Fixed-step integration, collisions, auto-kick, goal detection — pure functions over state, unit-testable |
| `GameHub : Hub` | SignalR | Connection lifecycle + input only: `JoinRoom(roomId)` (membership check, add to group, returns full room state) and `HandInput(int hand, int dir)`; snapshot broadcast via group per room |

- **Two entry points, one authority**: the hub handles what must ride the JS connection
  (join, input, disconnects); the Blazor page invokes everything else (seats, resets, end,
  rematch) **directly on the `GameRoom` API** — same process, no JS interop, user identity
  from the circuit's auth state. Both paths funnel into the same thread-safe room methods.
- **WASM-migration guardrails** (see decision log / §7): both adapters stay **logic-free** —
  hub methods and the Blazor page's button handlers are one-liners over the `GameRoom` API
  (including the membership check — the existing `TeamMembershipService.IsMemberAsync`,
  callable from both), so exposing the
  remaining lobby ops as hub methods later is mechanical. `GamePhysics`, the protocol DTOs
  (`RoomState`, snapshot), and the constants live in their own folder with **no server-only
  dependencies** (no EF, no ASP.NET/hub types) — extractable into a shared project for a
  WASM client without rewrites.

- **Program.cs additions**: `AddSignalR()`, `MapHub<GameHub>("/hubs/game")`,
  `AddSingleton<GameRoomManager>()`.
- **Auth**: `[Authorize]` on the hub — the Identity cookie flows automatically for
  same-origin JS SignalR connections, and the app's **default authorization policy**
  (authenticated + `NameIdentifier` claim, set in Program.cs) applies to the hub too, so
  admin-only cookie sessions are rejected for free (the admin identity deliberately
  carries no `NameIdentifier` claim). `Context.UserIdentifier` yields the user id.
  `JoinRoom` verifies team membership via the existing
  `TeamMembershipService.IsMemberAsync` (it depends only on
  `IDbContextFactory<AppDbContext>`, so it resolves fine inside a hub) before adding the
  connection to the room's SignalR group.
- **Broadcasting**: rooms aren't hubs — `GameRoom` pushes snapshots and `RoomState`
  through an injected `IHubContext<GameHub>` (hub instances are transient; `IHubContext`
  is the singleton-friendly handle to a room's group).
- **Simulation**: 60 Hz fixed timestep; snapshots broadcast at 20 Hz. Input is held-key
  state per hand (`dir ∈ {-1, 0, +1}`, clamped; ignored from non-seated users); the server
  maps hand → rod(s), integrates rod positions and clamps to travel range. Fully
  authoritative — clients never report positions. Held-hand state is kept per user and
  cleared when their seat is vacated; on a seat swap it carries over (hands are
  seat-relative — the same held keys just drive the new rods). Don't trust
  `PeriodicTimer` for exact 16.7 ms ticks — measure elapsed time (`Stopwatch`) and run a
  fixed-step accumulator.

### 4.3 Snapshot payload

Small and flat — sent 20×/s to the room's group:

```
{ t: n,               // server tick — clients build the interpolation timeline from
                      // this instead of packet-arrival times (jitter-proof); doubles
                      // as a staleness/debug counter
  b: [x, y],          // ball position
  v: [vx, vy],        // ball velocity (for extrapolation)
  o: [8 × f],         // 8 rod offsets 0..1 (figure positions derive client-side)
  s: [a, b],          // score
  st: 0|1|2,          // 0 = waiting, 1 = playing, 2 = game over
  r: n,               // reset counter — increments on any ball teleport (goal reset,
                      // manual/stall reset, kickoff, rematch); when it changes, the
                      // client snaps instead of interpolating across the discontinuity
  k: [rod, fig],      // most recent auto-kick's figure ([-1,-1] before any) — drives the
                      // client swing animation; kicks inside one 50 ms snapshot window
                      // may coalesce (cosmetic only)
  kt: n }             // server tick of that kick — the client schedules the swing on the
                      // same tick timeline as the ball, so foot and ball line up
```

Figure positions are derived client-side from static table geometry + rod offsets, so the
payload stays ~100 bytes of JSON.

**Lobby state rides the wire as one `RoomState` DTO.** Any lobby change (seat taken,
viewer joined, game ended, state transition) broadcasts the **same full DTO** to the group
rather than bespoke delta events: seats + occupants (names, avatars) with a per-seat
**connected/grace flag** (drives seat-list graying and the "Waiting for Bob to
reconnect…" overlay, §3.4), viewer list, score, game state, and a **`closed` flag** —
set on the final broadcast when the room is destroyed (End game, idle cleanup), so a
pure-wire client learns the room is gone (the Blazor page also hears that in-process
via `Changed`) — a few hundred bytes, a few times a minute. Full-state-replace keeps clients
idempotent and trivial (no event ordering, no missed-delta bugs), and it makes the wire
protocol **complete**: snapshots + `RoomState` + `JoinRoom` carry everything a client with
no in-process access would need — the future-WASM guarantee (§7).

**`JoinRoom` returns `RoomState` plus the static config** — the table geometry / physics
constants the renderer and predictor need (table size, rod x-positions, figure
counts/spacing, radii, rod speed). Late joiners and reconnects initialize from it instead
of waiting for events, and the constants live in exactly one place (the server).

### 4.4 Client (game.js)

- Plain JS module + a **vendored** `@microsoft/signalr` browser build under
  `wwwroot/lib/signalr/` (mirrors the vendored Bootstrap — no bundler, no CDN), loaded
  only by the game page (game.js dynamic-imports it; the UMD browser build attaches
  `window.signalR`).
- Renders on `<canvas>` with `requestAnimationFrame`; draws table, rods, figures, ball,
  score. Interpolates between the last two snapshots (render ~100-150 ms behind server
  time) so 20 Hz updates look like smooth 60 fps.
- **Own-rod prediction**: the two rods you control are rendered from **locally integrated**
  held-key state (same rod-speed/travel constants, provided by `JoinRoom`), so they respond
  instantly instead of ~150 ms late; local positions are continuously nudged toward the
  server's snapshot values to correct drift. Ball and all other rods stay purely
  interpolated. Server authority is untouched — this is display-only.
- **Input** (keyboard, §2.2):
  - `keydown`/`keyup` listeners for `W`, `S`, `↑`, `↓` while seated; each state change
    sends one `HandInput(hand, dir)` hub message. No per-frame input traffic.
  - `preventDefault()` on the arrow keys while seated, so the page doesn't scroll.
  - Key-repeat events are ignored (held state is tracked, not repeated keydowns).
  - On blur/visibility loss, send `dir = 0` for both hands so a backgrounded tab doesn't
    leave a rod gliding into the wall.
- **Connection lifecycle**: `withAutomaticReconnect` gets an explicit delay array
  spanning the grace window (steady ~2 s retries for ~60 s) — the default schedule
  (4 attempts at 0/2/10/30 s, then permanent close) gives up while a 30 s grace is
  still running. On final `onclose`, and on initial-start failure (which automatic
  reconnect does not cover), show a "connection lost" notice with a retry/reload action.
- **Blazor interop**: the page component passes context to
  `game.init(canvasEl, { hubUrl, roomId, userId })` on first render and calls
  `game.dispose()` on teardown (which stops the hub connection → graceful leave, §3.5).
  `userId` is used only for display decisions — tint own rods, enable key listeners when a
  `RoomState` update says this user sat down; the server never trusts it. Lobby state that affects
  the Blazor UI (seats, buttons, overlays) does **not** round-trip through JS: the page
  keeps its own subscription to `GameRoomManager.Changed` (server-side, same process) and
  re-renders from room state directly. No JS→Blazor interop needed.

### 4.5 Page & routes

| Route | Page | Auth |
|-------|------|------|
| `/team/{teamCode}/game` | Live game (canvas + lobby sidebar) | Team member |

Single page for players and viewers — what you see depends on whether you hold a seat.

### 4.6 Physics constants (initial values, all tunable)

| Constant | Value | Notes |
|----------|-------|-------|
| Logical table size | 1200 × 700 units | Canvas scales to fit, aspect preserved |
| Goal mouth height | 230 units | Centered |
| Rod x-positions | 75, 225, … , 1125 | 8 rods evenly spaced 150 apart (x = 75 + 150·k), symmetric; GK rods 75 from their goal line |
| Ball radius | 14 | |
| Figure body radius | 16 | Collision circle per figure |
| Figure spacing | tableHeight / figureCount | Per rod — full-height coverage, no dead lanes, no overlap (§2.1) |
| Tick rate | 60 Hz | Fixed step |
| Snapshot rate | 20 Hz | |
| Rod speed | 650 units/s | Vertical travel while key held |
| Kick speed | 900 units/s | Horizontal, toward opponent goal |
| Max deflection | ±500 units/s | Vertical, scaled by contact offset |
| Rod momentum transfer | 0.35 | Fraction of rod velocity added to ball on kick |
| Kick cooldown | 200 ms | Per figure |
| Max ball speed | 1400 units/s | |
| Friction | ~0.3 /s velocity damping | |
| Wall restitution | 0.85 | |
| Kickoff speed | 250 units/s | Toward conceding side, small random angle |
| Stall reset | speed < 40 for 5 s, or untouched 15 s | |

Sanity check on the numbers: stepping fully through a figure between two ticks takes
more relative motion per tick than the contact-circle diameter (2 × (14 + 16) = 60
units). Worst case — ball at max speed meeting a rod sliding the other way — is
(1700 + 650) / 60 ≈ 39 units/tick, a ~1.5× margin (the ball alone is ~28). Off-center
grazes have shorter chords and can occasionally be stepped past, but a missed graze is a
near-miss, not a pass-through. If max ball speed is ever raised past ~2500, add
substepping. Wall bounces and goal detection test the prev→new movement segment, not
just the end position, so the goal line can't be tunneled regardless.

Tuning these (kick strength, cooldown, friction) is where the game gets its feel — expect
iteration after the first playable build.

---

## 5. UI/UX

### 5.1 Game page layout

```
┌──────────────────────────────────────────────────────────────┐
│  Team A  3 : 5  Team B          👁 4 viewers      [End game]  │
├──────────────────────────────────────────────┬───────────────┤
│                                              │ SEATS         │
│                                              │ 🦁 A-Defense  │
│                <canvas>                      │ ➕ A-Attack   │
│         (table, rods, ball, score)           │ 🐺 B-Defense  │
│                                              │ 🦊 B-Attack   │
│                                              ├───────────────┤
│                                              │ VIEWERS 👁 4  │
│                                              │ 🐧 Pete       │
│                                              │ 🦄 Una  …     │
│                                              ├───────────────┤
│                                              │ [Reset ball]  │
│                                              │ [Reset score] │
│                                              │ [Leave seat]  │
├──────────────────────────────────────────────┴───────────────┤
│      W/S — your left rod · ↑/↓ — your right rod              │
└──────────────────────────────────────────────────────────────┘
```

- Figures are drawn as **top-down foosball men** — shoulders along the rod, head on top —
  not plain circles. On an auto-kick the kicking figure plays a ~0.3 s **swing**: the foot
  sweeps out from behind the rod through the ball toward the goal it attacks (left→right
  for side A, right→left for B), scheduled on the snapshot tick timeline (`k`/`kt`, §4.3)
  so it stays in sync with the interpolated ball.
- Seats show claimed-player avatar + name; empty seats show ➕ (clickable if you may sit).
- Viewers listed with avatar + name below the seats.
- Your own seat is highlighted; your rods are tinted on the canvas so you know what you control.
- A one-line controls legend sits under the canvas (only shown while you hold a seat).
- Team colors follow the app convention: side A yellow (`#ffc107`), side B blue (`#0d6efd`).
- Status overlays follow the fast/slow split of §4.1: the transient "GOAL!" flash is drawn
  **in the canvas** (game.js flashes when a score component **increments** between
  snapshots — a decrease is a reset/rematch, no flash); the "Waiting for opponents…" /
  "Waiting for X to reconnect…" (§3.4) and "🏆 Alice & Bob win 10 : 6" states are
  **Blazor HTML overlays** positioned over the canvas — they're driven by slow lobby/room
  state, need names + avatars, and host the **Rematch** button.
- `GameRoom` raises `Changed` on goals and state transitions too (a few events per minute),
  so the Blazor header score and overlays stay current without touching the frame loop.

### 5.2 Header & dashboard integration

- `TeamLayout` header, next to **New Match**:
  - No room: `🎮` button → starts a game (creates room, seats you, navigates).
  - Room exists: pulsing `🔴 2/4` badge → navigates to the game page.
- Dashboard banner card while a room exists (below the season card): seated avatars, free
  seats, viewer count, Join/Watch button. Updates live via `GameRoomManager.Changed`.

### 5.3 Mobile

- v1 is **desktop/keyboard-first** — playing requires a keyboard. Phones and tablets can
  still join as **viewers** (canvas is responsive and view-only works fine on touch).
- Touch controls are a fast-follow, not v1: split the canvas into left/right halves —
  vertical drag on the left half drives the `W`/`S` rod, right half drives the `↑`/`↓`
  rod. Touch would send an absolute target offset; the server moves the rod toward the
  target capped at rod speed, so keyboard and touch players stay physically equivalent.

---

## 6. Out of Scope (v1)

- **No persistence** — nothing is written to the database. No new entities, no migrations.
- **No ELO / match recording** — results are bragging rights only (future option below).
- **No cross-team or public games** — team members only.
- **No matchmaking, invitations, or notifications** — discovery is the header badge/banner.
- **No automated tests** — the solution has no test project today and v1 doesn't add
  one (decided in spec review). `GamePhysics` stays pure/dependency-free so tests can be
  added later without refactoring.
- **No sound effects** (candidate for a fun follow-up).
- **No spectator chat / emotes.**
- **No touch/mobile play** — phones can watch, not play (touch scheme sketched in §5.3 as
  a fast-follow).
- **No key remapping** — W/S + arrows are fixed v1 defaults, revisited after playtesting.
- **Single server instance assumed** — in-memory rooms + SignalR groups need sticky
  sessions or Azure SignalR Service if the app ever scales out (it won't for ~10 users).
- **Hosting requirement**: WebSockets must be enabled (already required by Blazor Server).

---

## 7. Future Extensions (design kept compatible)

1. **Joinable from outside the team** — rooms already have their own `RoomId` and an
   authorization step at `JoinRoom`; a share-link mode (à la `ShareToken`) or cross-team
   lobby can be added without changing the room/sim model.
2. **Record result as a real Match** — after a 2v2 game where all four seats map to claimed
   players, offer "Save as match" (feeds the existing `MatchService`/ELO pipeline). Needs a
   team decision on whether remote games should affect the ladder.
3. **Multiple concurrent rooms per team** — `GameRoomManager` keys rooms by `RoomId`
   internally; the "one per team" rule is a v1 policy, not a structural limit.
4. **Power-ups / silly modes** — tiny ball, drunk mode (inverted controls), 2-ball chaos.
5. **Sounds + goal celebrations.**
6. **Blazor WASM game client** — rejected for v1 (decision log) but deliberately kept
   cheap. The wire protocol is already complete (snapshots + `RoomState` + `JoinRoom`, §4.3)
   and the v1 canvas client lives entirely on it. Migration steps: (1) expose the lobby
   mutations (`TakeSeat`, `SwapSeat`, `LeaveSeat`, `ResetBall`, `ResetScore`, `EndGame`,
   `Rematch`) as one-line hub wrappers over the existing `GameRoom` API; (2) move
   `GamePhysics` + protocol DTOs + constants into a shared project — they carry no server
   dependencies by design (§4.2); (3) rewrite the renderer/input layer in C# — the only
   real work, and it stays small because game.js is a dumb terminal (render, interpolate,
   key capture, own-rod prediction; zero game rules).

---

## 8. Decision Log

Settled during spec review (2026-07-06):

| Topic | Decision |
|-------|----------|
| Kick physics | Best value/effort: contact-offset deflection **plus rod momentum** (§2.3) — momentum is nearly free since rods are server-integrated |
| Win condition | First to 10; game-over overlay names the winners, with Rematch/reset option (§2.4) |
| Permissions | Fully friendly — **any seated player** can end game, reset score, reset ball (§3.6) |
| Controls | Keyboard, **screen-relative**: `W`/`S` = left-on-screen rod, `↑`/`↓` = right-on-screen rod; in 1v1 the role pairs (GK+DEF, MID+ATK) go to the hand nearer them on screen (§2.2). Goal-relative was the original design and was flipped after the first hands-on test |
| Timeouts | 30 s disconnect grace, ~2 min empty-room cleanup — try as spec'd, tune if annoying (§3.5) |
| Seat liveness | One rule: a seat whose user has zero live hub connections is grace-held (30 s) — covers mid-game drops, the creator seated before their JS connects, and take-seat races (§3.5) |
| Mid-game dropouts | Grace hold **pauses the ball in place** (no free goals against a dropped opponent); a fully vacated side → `waiting`, ball parked at center, **score kept** (§3.4) |
| Viewers | Listed with names **and** avatars, plus count (§3.7, §5.1) |
| Input feel | **Own-rod client prediction is in v1** — your two rods render from local key state instantly, nudged toward server snapshots; ball and other rods stay interpolated (§4.4) |
| Touch controls | **Fast-follow, not v1** — keyboard-only play, phones watch; absolute-target scheme stays as designed in §5.3 |
| Input protocol | Hand-level (`left`/`right`), not rod numbers — server maps hand → rod(s), which makes 1v1 pairing purely server-side and ownership unspoofable (§2.2) |
| Hub vs circuit | `GameHub` carries only `JoinRoom` + `HandInput`; all lobby/lifecycle mutations go through `GameRoom`'s thread-safe API straight from the Blazor circuit (§4.2) |
| Client tech | **Plain JS canvas client — Blazor WASM considered and rejected for v1.** A WASM game page (Blazor Web App mixed render modes) requires a separate client project + client-side auth plumbing, cannot call `GameRoomManager` in-process (forcing all lobby ops back onto the hub, undoing the split above), and still needs JS interop for the 60 fps canvas loop — the bulk of the JS. Revisit if the client ever grows real logic (client-side ball prediction/rollback, bots, layered input schemes) where sharing the C# `GamePhysics` would pay off. **The design keeps that migration cheap by construction** — complete wire protocol, logic-free adapters, dependency-free physics/DTOs (§4.2 guardrails, §4.3, path in §7.6) |

## 9. Open Questions (remaining)

1. **First playtest checklist** — things expected to need tuning rather than re-deciding:
   kick speed vs. rod speed balance, momentum transfer factor (0.35), cooldown feel,
   friction/stall thresholds, and the strength of the prediction correction nudge (§4.4).
   (The hand-assignment question originally listed here is resolved: goal-relative was
   flipped to screen-relative after the first hands-on test, §2.2.)

## 10. Implementation notes (2026-07-06)

Small details settled while building v1 — all within the spirit of the spec:

- **One kickoff pause everywhere**: *every* entry of the ball into motion (game start,
  opponents arriving, goal kickoff, reconnect resume, rematch) uses the same ~1 s pause
  before the pending velocity applies. The spec named the pause only for goals and
  reconnects; unifying it means players always get a beat to orient. Manual/stall ball
  resets stay immediate.
- **Reset score during game over** behaves like Rematch (score 0:0, fresh kickoff, seats
  kept) instead of leaving a 0:0 game-over screen.
- **Header badge** reads `● n/4 · Join` (pulsing dot; `Watch` when full) and links to the
  game page; the no-room state is a `🎮` button with a "Start live game" tooltip.
- **Kickoff angle**: ±22.5° around the horizontal.
- **Canvas sizing**: besides a `ResizeObserver`, the render loop cheaply re-checks the
  container width each frame — observers proved unreliable for some layout changes and
  zero-width initial mounts.
- The canvas carries `data-room-id` for diagnostics.
- **Figure counts corrected after first review**: 1 GK / 2 DEF / 5 MID / 3 ATK per team
  (the spec originally drew a 3-goalie table). Only the GK rods changed; with the
  `H/figureCount` spacing rule the single goalie gets full-height travel.
- **Vendored sourcemap**: `signalr.min.js.map` ships alongside the client (mirrors the
  bootstrap convention) so devtools' map request resolves instead of 404-ing. (Historically
  this mattered more: a since-removed `LegacyTeamRedirect` root-level catch-all turned every
  404 — including that map request — into a `NavigationException`. That route is gone;
  unmatched paths now return a clean 404, but keeping the sourcemap avoids the 404 entirely.)
- **Hands flipped to screen-relative** after the first hands-on test (§2.2, §8): `W`/`S` =
  left-on-screen rod(s), arrows = right-on-screen. Only `SeatMap` tables and their game.js
  mirror changed; the wire protocol (hand 0/1) is untouched.
- **Figure visuals + kick swing** (added after first review): top-down foosball men with a
  foot-swing animation on auto-kick; the snapshot gained `k`/`kt` (§4.3) so the swing is
  server-authoritative and tick-synced rather than guessed from velocity changes.
- Verified end-to-end: wire protocol (20 Hz snapshots, 3 ticks/snapshot), hand→rod mapping
  incl. 1v1 pairs, seat grace/resume/leave flows, physics (kick, momentum, walls, goals,
  no tunneling at max speeds), idle cleanup and End game — via a browser session plus a
  two-user `GameRoom` harness driving the real tick loop.

---

## 11. Ball-control skills (§skill)

Added after the first playtests to give trapping/passing depth beyond the auto-kick. All of it is
server-authoritative in `GamePhysics`; the client just captures keys (`Catch`/`Space` hub calls) and
renders. The `(§skill)` markers throughout the code point here.

- **Catch/trap — hold `Shift`.** Arms *every* rod the caller drives (one hand-agnostic flag, not
  per-hand), so the ball traps on whichever of your figures it reaches and charges while held.
  Releasing fires it toward the opponent goal, power scaled by charge time (soft tap → placing pass,
  full hold → cannon). A green glow marks armed rods; a draining ring around a trapped ball shows the
  remaining hold window before it auto-fires.
- **Lane pass — `Space` while sliding.** Hops a trapped ball to the adjacent man on the *same* rod in
  the slide direction. Stays trapped (a controlled hop between your own figures, not an interceptable
  toss); the hold timer resets, buying a fresh setup window on the new man.
- **Goalie cannon.** Power comes from *charge*, not a flat bonus, and the keeper charges it **by
  holding `Space`** — hold to build, release to launch (so its strength is deliberate, not automatic).
  An **uncaught** goalie touch is plain-strength (the ordinary auto-kick). A **caught** shot ramps from
  regular `KickSpeed` (a quick tap → a normal clearance) up to `KickSpeed × (1 + GoalieCaughtPowerBonus)`
  (1.35 → ~1645 u/s, just under the raised `MaxBallSpeed` 1700 cap — a genuine cannon) over
  `GoalieMaxChargeSeconds` (1.5 s). Outfield rods keep their snappy auto-charge — they power up as they cradle the ball (catch
  key held), on the 0.6 s / `KickPowerBonus` (0.4) ramp — and use `Space` only to pass.
  - Two clocks back this, so the keeper can hold without auto-charging: `HoldSeconds` (always ticking →
    the `GoalieTrapTimeoutSeconds` 5 s auto-fire backstop, drives the draining hold ring) and
    `ChargeSeconds` (advances only while charging → shot power, drives the power ring). A lane pass
    resets both.
  - Two rings on the trapped ball read this: an inner **hold** ring that *drains* over the window
    (side-colour, red near expiry) and, for the goalie only, an outer **power** ring that *fills*
    green→red while `Space` is held. Snapshot fields: `ch` (hold remaining), `pw` (power fraction).
  - Input plumbing: `Space` is a held key (down/up) — `RodSpace` per rod feeds the goalie charge; the
    room turns the press edge into an outfield pass and the release edge into the goalie launch.
- **Back-pass — `Space` when a rod sits behind you** (toward your own goal), i.e. not sliding into a
  lane pass. Ejects the ball as a soft `BackPassSpeed` (550) toss toward the rod behind (DEF→GK,
  MID→DEF, ATK→MID). Unlike a lane pass it *leaves* the rod and is a real ball: an armed rod behind
  can trap it, but an **opponent rod in the lane can intercept** — and geometry decides the risk. The
  rods interleave A/B/A/B, so DEF→GK (x225→x75) crosses no enemy rod and is safe, while ATK→MID
  (x825→x525) passes the opponent's MID at x675 and can be picked off. The set-piece it enables:
  trap on defense → back-pass to the (armed) keeper → release the cannon up-field.
- **Bots** don't use any of this — they only steer rods toward the ball and rely on the auto-kick
  (`GameBot`), so the skills are a human-only edge.

---

## 12. Latency instrumentation (§12)

Added to measure the real end-to-end latency budget before spending effort tuning it (remote players,
Azure Basic single instance). **Captured only while a game is actively `playing`** — the server tick
loop gates on `_phase == Playing`; the client gates on seated (`myRods`) + `playing`. Code map:
`Game/Core/SampleWindow.cs` (percentile summarizer), `Game/GameTelemetry.cs` (`[LoggerMessage]`
events), `GameHub.Ping`/`ReportStats`, `GameRoom.RecordClientStats` + the tick-cadence block in
`RunLoopAsync`, and the `// §12` block in `wwwroot/js/game.js`.

**Signals — one per row of the latency budget**, summarized over ~10 s windows (percentiles computed
at the edge because Azure Monitor flattens OTel histograms to avg/min/max/count only). The client's
per-window report (`ReportStats(ClientStatsDto)`) bundles the four client signals; the server logs
tick + connection separately:

- **Client RTT** (`signal=client`, `rtt*`) — the JS client times a no-op `Ping` hub round trip every
  2 s (real application-level latency through SignalR). → network / region.
- **Client snapshot gap** (`gap*`) — inter-arrival time between snapshots as the client sees them
  (jitter → sizes the interpolation buffer). → stream jitter / snapshot rate.
- **Client frame interval** (`frame*`) — render frame spacing; ~16.7 ms is a healthy 60 fps, higher
  means a slow device or throttled tab. Recorded only while visible; resume spikes (>1 s) skipped.
  → client-side render jank.
- **Client extrapolation rate** (`extrapFrames`/`sampledFrames`) — fraction of frames with no future
  snapshot to interpolate toward, so the ball had to be extrapolated. A rising fraction means the
  buffer is too tight for the jitter → directly informs any future adaptive-interp-delay tuning.
- **Server tick cadence** (`signal=tick`, `tick*` + `stalls`) — actual loop iteration gap vs the
  16.67 ms target, plus a `stalls` count (gap > 2× target). → scheduler/GC/CPU stalls on a contended
  instance.
- **Server broadcast time** (`send*`) — how long the group snapshot send takes; separates server-side
  send/backpressure from a sim-loop stall (both otherwise look like "server lag").
- **Connection transport** (`signal=conn`, `transport=`) — the negotiated SignalR transport
  (`WebSockets` / `ServerSentEvents` / `LongPolling`), logged once per connection. A silent fallback
  to SSE / long polling wrecks latency — this turns "mystery high RTT" into a named cause.

**Wire → App Insights.** Emitted as structured `ILogger` events (rendered message starts with
`GameLatency`, `signal=client|tick`); exported to the existing Application Insights via the
**Azure Monitor OpenTelemetry distro** (`Azure.Monitor.OpenTelemetry.AspNetCore`). Locally they just
print to the console. Custom telemetry is diagnostic only — the server never trusts the client numbers.

**Enabling in Azure (deliberate flip — off by default):**
1. App setting `Telemetry__UseAzureMonitorOpenTelemetry = true`.
2. App setting `ApplicationInsightsAgent_EXTENSION_VERSION = disabled` — the in-process distro
   **replaces** App Service codeless auto-instrumentation (they compete for the same sources; MS
   guidance). The distro still collects requests/dependencies/logs, so nothing is lost.
3. `APPLICATIONINSIGHTS_CONNECTION_STRING` must be present (it already is under codeless).

**Analyzing (KQL over `traces`).** customDimensions keys are the `[LoggerMessage]` template names
(`Seat`, `RttMean/P50/P95/Max/N`, `GapMean/P95/Max/N`, `FrameMean/P95/Max`, `ExtrapFrames`,
`SampledFrames`, `TickMean/P95/Max`, `Stalls`, `SendMean/P95/Max`, `Players`, `Transport`, `UserId`):

```kusto
// Per-seat client picture — is it network (rtt), stream jitter (gap), device (frame), or buffer (extrap)?
traces
| where message startswith "GameLatency signal=client"
| extend d = customDimensions
| extend seat = toint(d.Seat)
| summarize avgRttMs = avg(todouble(d.RttMean)),   worstRttP95Ms = max(todouble(d.RttP95)),
            avgGapMs = avg(todouble(d.GapMean)),   worstGapP95Ms = max(todouble(d.GapP95)),
            avgFrameMs = avg(todouble(d.FrameMean)), worstFrameP95Ms = max(todouble(d.FrameP95)),
            extrapPct = 100.0 * sum(toint(d.ExtrapFrames)) / sum(toint(d.SampledFrames))
        by seat, bin(timestamp, 1h)
| order by bin_timestamp asc, seat asc

// Server health — sim-loop stalls vs broadcast/backpressure (both otherwise look like "server lag").
traces
| where message startswith "GameLatency signal=tick"
| extend d = customDimensions
| summarize avgTickP95Ms = avg(todouble(d.TickP95)), worstTickMaxMs = max(todouble(d.TickMax)),
            totalStalls = sum(toint(d.Stalls)),
            avgSendP95Ms = avg(todouble(d.SendP95)), worstSendMaxMs = max(todouble(d.SendMax))
        by bin(timestamp, 1h)
| order by bin_timestamp asc

// Transport sanity — anyone NOT on WebSockets? (SSE / long polling = the latency is the transport)
traces
| where message startswith "GameLatency signal=conn"
| extend d = customDimensions
| summarize connections = count() by transport = tostring(d.Transport), team = tostring(d.TeamId)
```

Verified end-to-end (2026-07-08) via a local bot-assisted play session: all three events emit with
correct dimensions; `gapMean≈50 ms / gapN≈200` matched the 20 Hz snapshot rate exactly, `rttN=5`
matched the 2 s ping spacing, `frameMean≈16.7 ms / sampledFrames=600` matched 60 fps over the window,
`extrapFrames=0` and `sendMean≈0.04 ms` and `stalls=0` locally, and `transport=WebSockets`.

### Player-facing ping readout & player options

Separate from the server-side telemetry above, players can see **their own** live ping. A **Player
options** card in the lobby sidebar (visible to seated players and viewers alike) toggles **Show ping**,
which draws a colour-coded `● N ms` in the top-left of the canvas (green <60 / amber <120 / red ≥120).
It's the smoothed (EMA) RTT from the same `Ping` probe — which runs whenever the option is on *or*
telemetry is measuring, so viewers and waiting players get it too; telemetry recording stays
playing-only. Caveat by design: own rods are client-predicted, so this is **connection RTT, not control
lag** — the ~175 ms interpolation/quantization floor is not in this number.

**Player options are client-local, not room state** (contrast §game-options, which are
server-authoritative and shared): persisted per browser in `localStorage` (`fotbalek.playerOptions`),
owned by `game.js`, surfaced through the Blazor toggle via **Blazor→JS interop only** (`setPlayerOption`
/ `getPlayerOption` — never JS→Blazor, keeping the §4.4 rule). To add a future player option: add a key
+ default to `playerOptions` in `game.js`, read it in the render/loop, and add a toggle row in
`LiveGame.razor` bound the same way as **Show ping**.
