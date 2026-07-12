# Team Chat

**Status**: **Implemented 2026-07-10** (per the §7 file map; spec originally drafted
2026-07-08, revised 2026-07-10 to a single **global chat dock** across all authenticated
pages — see §12). This spec is meant to be iterated
on — §12 records the decisions settled with the user; §13 confirms the open questions are resolved.
Cross-references to existing code (entities, service signatures, DI lifetimes, the
`PresenceTracker`/`GameRoomManager` event pattern, `db.Database.Migrate()`, `TimeZoneService`,
`TeamMembershipService.GetMembershipOverviewForUserAsync`, the `fotbalek.playerOptions`
localStorage precedent, live-game §§3.7/4.1) were validated against the `src/Fotbalek.Web/` tree
on 2026-07-08 and re-validated 2026-07-10 (badge liveness wiring corrected — see §5.2). A second
full validation pass on 2026-07-10 confirmed all code cross-references and fixed: emoji reaction
uniqueness needs a binary collation on SQL Server (§2.2), the join-floor lookup needs a
`(TeamId, CreatedAt)` index (§2.1/§4.7), dock/`TeamLayout` parent-child wording (§5.2), touch
Enter-key behavior (§6.2), reactions on tombstones rejected (§4.6), and the subscribe-before-load
race on panel open (§4.7).

**Planned code map** (nothing exists yet; paths relative to `src/Fotbalek.Web/`): `Data/Entities/ChatMessage.cs`,
`ChatMessageReaction.cs`, `ChatReadState.cs` + `AppDbContext` config + one migration;
`Services/ChatService.cs` (DB ops), `Services/ChatNotifier.cs` (singleton in-process pub/sub +
ephemeral typing state), `Services/ChatUiState.cs` (scoped per-circuit dock UI state);
`Components/Chat/*` (`ChatDock.razor` + scoped `ChatDock.razor.css` — the global launcher +
team-switcher shell that also raises banners; `ChatConversation.razor` — one team's message
list/composer; `ChatMessageView.razor`; `EmojiPicker.razor` + vendored
`wwwroot/lib/emoji-picker-element/`);
`wwwroot/js/chat.js` (tab title, visibility/focus reporting); the dock is rendered by **both**
`Components/Layout/MainLayout.razor` and `Components/Layout/TeamLayout.razor` (so it's on every
authenticated page), with per-team unread badges also on `Components/Pages/Home.razor`, plus an
`@using Fotbalek.Web.Components.Chat` in `Components/_Imports.razor`.

---

## 1. Description

A lightweight, persistent, team-scoped chat surfaced through a **single global dock**. Every team
has exactly one chat channel; all members can post text and emoji, react to messages, @mention
teammates, and see who is typing. The dock is a floating launcher + panel present on **every
authenticated page** (whenever the user has at least one team they've claimed a player in), so a
user can read and post to **any of their teams from anywhere** — the panel carries its own
**team switcher** and is independent of which page (or team) they happen to be viewing. A member
sees only the history from the moment they **joined the team**, sees per-team unread badges (in
the dock's team switcher, plus a total on the launcher, and secondary copies on the nav team
switcher and the overview's team list), and gets an in-app banner whenever a message lands in a
team that isn't the currently open+focused conversation. Because the dock is everywhere, those
signals work on **all** authenticated pages — no team-page-only gap. **Browser / OS notifications
remain out of scope for v1** — they belong with a future app-wide notifications feature (§10/§11);
v1's signals are all in-app (banner + badges + tab title, no sound).

**Design principle** — match the rest of the app, not the live game. Chat *content* is
something people expect to persist, so unlike the ephemeral in-memory `GameRoom`, chat is
**DB-backed** (like matches/seasons). Real-time delivery, however, reuses the app's existing
Blazor Server circuit and the `PresenceTracker` pattern (an in-process singleton raising
events that circuits subscribe to) — **no new SignalR hub** (contrast the live game, which
needed its own hub only for a 60 fps canvas; see §3.1). PoC spirit otherwise: single server
instance assumed, no over-engineering.

**Scope anchors in the existing model:**
- A chat channel == a `Team` (one per team, keyed by `Team.Id`).
- Who may participate == `TeamMembership` rows for that team.
- "History since you joined" == messages created at/after `TeamMembership.JoinedAt` (§4.7).
- Sender display name + avatar == the sender's claimed `Player` in that team, resolved at
  render time — the same rule the live game uses (`PlayerService.GetUserPlayerInTeamAsync(teamId,
  userId)`, live-game §3.7).
- **The dock lists only teams where the current user has claimed a `Player`** (settled §12). The
  set — team, the user's join floor, and the user's `Player` per team — comes from the existing
  `TeamMembershipService.GetMembershipOverviewForUserAsync(userId)`, filtered to `MyPlayer != null`
  (one query). A membership without a claimed player is a transient state anyway — the app already
  `forceLoad`-redirects members to `/team/{code}/claim` on first visit — so such a team simply
  doesn't appear in the dock until claimed. This keeps the invariant that **every dock participant,
  including the current user in each team they can chat in, has a `Player`**; the user-name
  fallback is purely defensive.
- The dock is **gated on `authenticated && ≥1 claimed team`**: on anonymous pages (login/register),
  admin-only sessions, or for a brand-new user with no claimed team yet, it renders nothing. This
  is orthogonal to `TeamLayout`'s own unclaimed-member claim gate, which still governs access to a
  team's *pages*.

---

## 2. Data Model

Three new entities. `int` keys and `DateTimeOffset` timestamps stored in **UTC**, matching the
repo's timezone policy (convert to local only at the UI boundary via `TimeZoneService`).

### 2.1 `ChatMessage`

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `int` PK | Identity — **monotonic**, doubles as the chronological ordering key and the unread watermark (§5). Avoids clock-skew ordering bugs. |
| `TeamId` | `int` FK → `Team` | `OnDelete(Cascade)` (a deleted team takes its chat). |
| `SenderUserId` | `int` FK → `AppUser` | Stable identity. Display name/avatar resolved live from the sender's `Player` in this team. `OnDelete(Restrict)` — no cascade from `AppUser`: there's no user-deletion path today (so Restrict never blocks a delete), it matches the repo's user/player FK convention (`MatchPlayer.Player` and the season player FKs are all `Restrict`), and it avoids a future cascade **diamond**. (Two cascade FKs into one table is fine on its own — `TeamMembership` already cascades from both `User` and `Team` — the hazard is two paths from *one* root: if user-deletion were added, an `AppUser→ChatMessage` cascade alongside `Team→ChatMessage→ChatMessageReaction` would give SQL Server two delete paths from `AppUser` to the reactions table, which it rejects. Keeping the user FKs `Restrict` sidesteps that.) |
| `Body` | `string` maxlen 2000 | Raw user text. **Never rendered as `MarkupString`** (§8). Emptied on delete. |
| `CreatedAt` | `DateTimeOffset` | UtcNow at insert. Used only for the since-joined floor (§4.7) and day grouping; ordering is by `Id`. |
| `IsDeleted` | `bool` | Soft delete → renders as a "message deleted" tombstone (§4.6). |
| `EditedAt` | `DateTimeOffset?` | Set on each author edit (§4.6, added 2026-07-10); null = never edited. Renders an "(edited)" marker. |

Indexes: `(TeamId, Id)` — serves history pagination *and* the unread count query — plus
`(TeamId, CreatedAt)` for the one-time join-floor lookup (§4.7), which filters on `CreatedAt`
and would otherwise degenerate to a scan of the team's messages. Reactions are a child
collection (2.2).

### 2.2 `ChatMessageReaction`

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `int` PK | |
| `MessageId` | `int` FK → `ChatMessage` | `OnDelete(Cascade)`. |
| `UserId` | `int` FK → `AppUser` | Reactor. Display resolved to their `Player` like senders. `OnDelete(Restrict)` — consistent with `ChatMessage.SenderUserId`; users aren't hard-deleted in this app, and Restrict keeps a single cascade path into this table (reactions still cascade-delete via `MessageId`). |
| `Emoji` | `string` maxlen 32 | A single emoji (may be multi-codepoint / ZWJ, e.g. 👍🏽). **Binary collation required** (`.UseCollation("Latin1_General_100_BIN2")` on the property): on SQL Server's default `SQL_Latin1_General_CP1_CI_AS` (the app runs on SQLEXPRESS/Azure SQL defaults), supplementary-plane characters — i.e. most emoji — have undefined collation weights and compare **equal** (`N'😀' = N'😂'` is true), so without it the unique index below would treat any two emoji from one user as duplicates and the toggle-off lookup would match the wrong row. |
| `CreatedAt` | `DateTimeOffset` | |

Unique index `(MessageId, UserId, Emoji)` — one of each emoji per user per message; adding an
existing one is a toggle-off. Index `(MessageId)` for load.

### 2.3 `ChatReadState`

Per-user, per-team unread watermark.

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `int` PK | |
| `UserId` | `int` FK → `AppUser` | `OnDelete(Cascade)`. |
| `TeamId` | `int` FK → `Team` | `OnDelete(Cascade)`. |
| `LastReadMessageId` | `int` | Highest `ChatMessage.Id` this user has read in this team. |

Unique index `(UserId, TeamId)` (mirrors `TeamMembership`). Row is created/updated the first
time the user reads the team's chat; before it exists, the effective watermark is the
**join floor** (§4.7), so a brand-new member's unread count is "everything since I joined."

### 2.4 Migration & DI

- One EF migration adds the three tables; applied on startup by the existing
  `db.Database.Migrate()` in `Program.cs`.
- `builder.Services.AddScoped<ChatService>();`
- `builder.Services.AddSingleton<ChatNotifier>();`
- `builder.Services.AddScoped<ChatUiState>();` (per-circuit dock UI state, §6.1/§7)

---

## 3. Real-time Architecture

### 3.1 In-process notifier, not a SignalR hub

Blazor Server already holds a live circuit (WebSocket) per open tab. Chat UI is plain HTML
rendered by Blazor — a handful of messages per minute, not a 60 fps stream — so the cheapest
correct design is the **`PresenceTracker` pattern**:

```
┌─ Server (single process) ─────────────────────────────┐
│ ChatNotifier (singleton)                              │
│   • event MessagePosted / MessageDeleted /            │
│     ReactionChanged / TypingChanged  (carry TeamId)   │
│   • ephemeral typing state (in-memory, auto-expiring)  │
│        ▲ raise                         │ subscribe     │
│ ChatService (scoped)                   ▼               │
│   • DB reads/writes ──▶ raises events   ChatDock       │
│                                         (in each       │
│                                          user circuit) │
└────────────────────────────────────────┼──────────────┘
                                          │ render diff over existing circuit
                                          ▼
                               Browser (Blazor DOM + chat.js for
                               tab title / in-app banner)
```

- `ChatService` (scoped, uses `IDbContextFactory<AppDbContext>` like every other service)
  performs the DB write, then calls `ChatNotifier.NotifyMessagePosted(teamId, dto)` etc.
- `ChatNotifier` (singleton) raises a small set of events. Each event carries its `TeamId`;
  subscribers filter for relevance. `ChatDock` (rendered by `MainLayout` and `TeamLayout`, so
  one instance per authenticated page) subscribes **once for all of the user's teams** and, in its
  handler, calls `InvokeAsync(StateHasChanged)` — exactly how `TeamLayout` already consumes
  `PresenceTracker.Changed` and `GameRoomManager.Changed`.
- **Why not reuse `GameHub`-style SignalR**: that added a second connection and JS client
  purely to keep the render tree out of a per-frame loop (live-game §4.1). Chat has no frame
  loop; a re-render per message over the circuit is negligible. Keeping it in-process also
  means membership/identity come straight from the circuit's auth state — no cookie-auth
  plumbing on a separate hub.

### 3.2 Event kinds

`ChatNotifier` exposes (all carry `int TeamId` so handlers can ignore other teams):

| Event | Payload | Consumers |
|-------|---------|-----------|
| `MessagePosted` | `teamId`, `ChatMessageDto` | The dock appends if it's the open conversation, else bumps that team's unread badge; drives the in-app banner + tab-title (§5). |
| `MessageDeleted` | `teamId`, `messageId` | Panels tombstone the message; unread badges refresh (deleted messages don't count as unread, §5.2). |
| `MessageEdited` | `teamId`, `messageId`, new body, `editedAt` | Panels swap the body in place and show the "(edited)" marker. No banner, no unread impact (added 2026-07-10). |
| `ReactionChanged` | `teamId`, `messageId`, updated reaction summary | Panels update the reaction bar. |
| `TypingChanged` | `teamId`, set of typing `userId`s | Panels update the "X is typing…" line. |
| `ReadStateChanged` | `teamId`, `userId`, new watermark | Two consumers: **(a)** badge sync — docks in the same user's **other** tabs/circuits recompute their unread badges after one tab marks read, ignoring *other* users' events (§5.1/§5.2); **(b)** the **seen-by** readout — the open `ChatConversation` folds every member's advance into a live watermark map to show who has seen the sender's latest message (§5.5). Raised by `ChatService` whenever a watermark advances. |

### 3.3 Typing state (ephemeral)

Typing lives only in `ChatNotifier` (a `ConcurrentDictionary<int teamId, Dictionary<int userId,
DateTimeOffset expiry>>`), never in the DB:

- Client calls `ChatService.SetTyping(userId, teamId, isTyping)`; the composer sends "typing"
  at most once every ~3 s while the user is actively typing, and "stopped" on send / blur.
- Server auto-expires an entry after ~6 s without refresh (guards against a dropped "stopped").
- `TypingChanged` broadcasts the current set **excluding the recipient's own id** (resolved
  per-subscriber, or broadcast-all-and-filter-client-side).

---

## 4. Feature Behaviors

### 4.1 Sending & receiving

- Composer is a growing `textarea`. **Enter** sends; **Shift+Enter** = newline (physical
  keyboards — see §6.2 for touch).
- Server trims, rejects empty/whitespace-only, and clamps to 2000 chars. Membership is
  re-verified on every send (`TeamMembershipService.IsMemberAsync`) — the circuit is
  authenticated but authorization is never assumed (§8).
- On success the message is persisted and `MessagePosted` fires; the sender's own panel shows
  it immediately (optimistic append is unnecessary — the round trip is in-process and fast).
- Light anti-spam throttle (in-memory, per user): e.g. max ~5 messages / 5 s, soft-fail with a
  toast. Tunable; see §9. **Where the counter lives:** because `ChatService` is **scoped**
  (per circuit, and by convention stateless — it only holds an `IDbContextFactory`), this
  per-user counter must live in the **singleton** — `ChatNotifier` or a small dedicated
  singleton — exactly like the typing state (§3.3). An instance field on the scoped service
  would be per-circuit, not per-user, so it wouldn't hold across a user's tabs (or survive
  re-resolution). `ChatService.Send` forwards to the singleton to check/record the throttle.

### 4.2 Emoji

- Unicode emoji typed directly work as-is (they're just text).
- An **emoji picker** button in the composer inserts at the cursor. v1 should **reuse an
  existing, self-hostable picker** rather than hand-curating a list — **`emoji-picker-element`**
  is the recommended fit: a dependency-free Web Component (MIT) vendored under `wwwroot/lib/`
  alongside Bootstrap and the SignalR client, driven through a thin JS-interop shim, giving the
  full Unicode set with search + categories for little effort. (Note the app is not strictly
  self-hosted today — Chart.js and Bootstrap Icons load from a CDN in `App.razor` — so a CDN
  import is an equally precedented alternative if vendoring proves awkward; vendoring is still
  preferred for the picker's ~MB emoji data.) (If it fights the Blazor circuit or the
  self-contained hosting, fall back to a small curated static array — the storage model is
  identical either way, since emoji are just text.)

### 4.3 Reactions

- Hovering (desktop) or tapping (touch) a message reveals a small "＋ react" affordance and a
  quick row of common emoji; picking one toggles the reaction for the current user.
- Under each message, reactions render as chips: `👍 3`, `😂 1`. The current user's own
  reactions are highlighted; clicking a chip toggles.
- Backed by `ChatMessageReaction` (unique per user+emoji+message → idempotent toggle).
  `ReactionChanged` pushes the updated summary live.

### 4.4 @mentions

- Typing `@` opens an inline autocomplete of the team's players (by `Player.Name`); selecting
  inserts `@Name`. The message stores raw text.
- At **render** time, substrings matching a player's name (in that message's team) after `@` are
  shown as a highlighted mention pill. Rendering is done by splitting text into typed segments
  (text / mention / emoji) and emitting Blazor markup per segment — **never** by injecting HTML
  (§8).
- A message that mentions **you** is emphasized in the list, and (if the panel isn't focused)
  contributes to the in-app banner text ("Alice mentioned you"). Name matching is longest-match
  against the known roster; ambiguity with spaces in names is an accepted limitation (§13).

### 4.5 Typing indicator

- "Alice is typing…", "Alice and Bob are typing…", "Several people are typing…" above the
  composer, driven by `TypingChanged` (§3.3). Purely ephemeral.

### 4.6 Editing & deletion

Both are **author-only** and live under a per-message **⋯ options menu** at the end of the
hover/tap action row (the quick-react emoji stay in the row itself; destructive/modifying
actions deliberately don't — revised 2026-07-10). No captain override in v1.

- **Editing** (added 2026-07-10, superseding the earlier delete-and-resend-only stance in §10):
  the menu's Edit swaps the body for an inline textarea (Save / Cancel / Esc). Server enforces
  `SenderUserId == currentUserId`, re-verifies membership, applies the same trim + 2000-char
  clamp as send, rejects edits to tombstones, and treats an unchanged body as a no-op. On
  success `Body` is replaced, `EditedAt` stamped, and `MessageEdited` fires — panels update in
  place, re-run mention segmentation, and show a muted "(edited)" marker (local-time tooltip).
  Edits raise **no banner** and never change unread counts.
- **Deletion**: the menu's Delete uses a two-click confirm ("Really delete?"). Server enforces
  `SenderUserId == currentUserId`; sets `IsDeleted = true`, empties `Body`, and
  removes the message's reactions. `MessageDeleted` fires; all panels render a muted
  "🚫 message deleted" tombstone in place (preserves conversation flow and reply context).
  Reacting to — or editing — an already-deleted message is rejected server-side.

### 4.7 History, pagination & the "since joined" floor

History is **kept forever** (settled §12) — no capping or archival. The UI never loads it all at
once: it **lazy-loads with infinite scroll**, newest page first and older pages fetched as the
user scrolls up (down to the join floor). The paging below is that infinite-scroll mechanism.

- **Join floor** — a member sees only messages created **at/after** their
  `TeamMembership.JoinedAt`. Concretely, the floor is expressed as a message id: `joinFloorId` =
  the `Id` of the newest message with `TeamId = T and CreatedAt < JoinedAt` (`TOP 1 ORDER BY
  CreatedAt DESC`; 0 if none) — equivalent to `max(Id)` over that range, since `Id` and
  `CreatedAt` agree on order (single server, identity + UtcNow-at-insert). All subsequent
  queries are then pure id comparisons (`Id > joinFloorId`). Computed once per panel open; a
  single seek on the `(TeamId, CreatedAt)` index (§2.1) — the `(TeamId, Id)` index alone
  wouldn't help, since the filter is on `CreatedAt`. (Existing members created before this
  feature have an early `JoinedAt`, so they simply see all current history — correct.)
- **Initial load** — newest page of ~50 visible messages (`Id > joinFloorId`, ordered
  `Id DESC`, reversed for display).
- **Older messages** — scrolling to the top loads the previous ~50 (`Id < oldestLoaded && Id >
  joinFloorId`) until the floor is reached ("beginning of chat since you joined").
- **New messages** — appended live; auto-scroll to bottom only if the user is already near the
  bottom, otherwise a "new messages ↓" pill appears. (The panel subscribes to `ChatNotifier`
  **before** running the initial page query and dedupes appends by message id, so a message
  landing between query and subscription is neither lost nor doubled.)

---

## 5. Unread & Notifications

### 5.1 Read semantics — when is a message "read"?

A user's `LastReadMessageId` for team T advances to the newest visible message id when the dock is
**open**, T is the **selected conversation**, and the tab is **visible/focused** — both on
select/open and as new messages arrive while those hold. It does **not** advance when the dock is
closed, another team is selected, or the tab is hidden/blurred. Focus/visibility comes from
`chat.js` reporting `document.hidden`/window focus into the dock. The write is **monotonic** — the
watermark only ever moves forward (`LastReadMessageId = max(existing, newId)`, upserting the row on
first read) — so multiple tabs of the same user, other selected teams, and out-of-order events can
never rewind unread. On advance, `ChatService` raises `ReadStateChanged` (§3.2) so the same user's
**other** open tabs drop their badges too. (Sending a message inherently advances the sender's
watermark — the send context satisfies open + selected + focused.)

### 5.2 Unread count & badges

`unread(user, team) = count(ChatMessage where TeamId = T and Id > effectiveWatermark and not
IsDeleted)`, where `effectiveWatermark = ChatReadState.LastReadMessageId ?? joinFloorId`.

All per-team counts come from one query, `ChatService.GetUnreadCountsAsync(userId) →
Dictionary<teamId,int>`, over the user's claimed memberships + read states. The **dock** runs this
query and keeps the result in `ChatUiState`'s unread cache, **recomputing** (not incrementing) on
`ChatNotifier` events — so a user's own sends, which advance their watermark (§5.1), never linger
as unread in their other tabs. `ChatUiState` raises a `Changed` event whenever the cache updates.
Surfaced as badges:

- **Dock launcher** — the **total** unread across all the user's teams on the floating button.
- **Dock team switcher** — a per-team badge next to each team in the in-panel switcher (the
  primary place you see *which* team has unread). Refreshed live on `ChatNotifier` events (the dock
  already subscribes).
- **Nav team switcher** (`TeamLayout` dropdown) — a secondary per-team badge on team pages.
  `TeamLayout` is the dock's **parent** (it renders `<ChatDock>`), and a child can't re-render
  its parent: it renders counts from
  `ChatUiState`'s cache and subscribes to `ChatUiState.Changed` — the same
  subscribe-and-`InvokeAsync(StateHasChanged)` pattern it already uses for `PresenceTracker.Changed`.
- **Home page** (`/`, "Your Teams" list) — per-team badges, so "in team selection you see which
  teams have unread." Same wiring as the nav switcher: render from `ChatUiState`, subscribe to its
  `Changed`; the dock — also present on Home — keeps the cache current. No banner wiring on Home —
  the dock handles that.

Efficiency: all counts are `Id > watermark` comparisons over the `(TeamId, Id)` index; live
push means no polling. For a handful of teams per user this is trivial; denormalizing a cached
per-team counter is a noted future option (§11).

### 5.3 Browser tab title

`chat.js` maintains the document title: total unread across the user's teams shown as a prefix,
`(3) Fotbalek — Rankings`. It strips any existing `(n) ` prefix before re-applying, and
re-applies whenever Blazor sets a new `<PageTitle>` — because Blazor's `<PageTitle>` overwrites
`document.title` on navigation, this needs a `MutationObserver` on the `<title>` element (there is
no existing title-management JS to hook). Cleared to the bare title at zero. Driven by `chat.js`,
which the dock loads on **every authenticated page** (§7), so the live title reflects unread
everywhere the user goes (not just team pages).

### 5.4 In-app banner (settled §12)

When a `MessagePosted` arrives for one of the user's teams and it is **not** immediately read
(§5.1) — the dock is closed, a *different* team is the selected conversation, or the tab is
backgrounded — an in-app banner fires. Because the dock is on every authenticated page, this works
**everywhere**, not just team pages. **No sound in v1**, and **no browser / OS notification in v1**
(deferred to a future app-wide notifications feature — §10/§11).

- **In-app banner** — a dismissible toast (Bootstrap toast, anchored near the launcher) reading
  e.g. "💬 {Team} — {Sender}: {preview}" or "{Sender} mentioned you in {Team}". Clicking it opens
  the dock with that team selected. It needs **no permission prompt** (pure in-app UI) and is the
  primary signal that a non-selected team has a new message. It is raised by the **`ChatDock`**
  itself (no separate host component): the dock is already mounted and subscribed on every page, so
  it emits the banner for any team that isn't the currently open+focused conversation.
- **Interop** is minimal and in-app only: Blazor→JS to set the tab-title unread prefix (§5.3);
  JS→Blazor to report visibility/focus for read semantics (§5.1). No `Notification` API, no
  permission request, and no `fotbalek.chatOptions` in v1 — those arrive with browser
  notifications later (§11).
- **Own messages never banner.** Each tab independently shows its own banner only for teams that
  aren't its currently-open conversation; because the banner is per-tab in-app UI there is nothing
  to dedup across tabs (the cross-tab OS-notification `tag` question is deferred along with the OS
  notifications themselves). Accepted for the PoC.

### 5.5 Read receipts ("seen by") _(added 2026-07-12)_

A quiet indicator under the **sender's own latest** non-deleted message shows who has seen it,
derived entirely from the existing read watermarks (§5.1) — **no new writes or events**.
`ChatService.GetReadWatermarksAsync(userId, teamId)` returns every member's `LastReadMessageId`
for the team (membership-gated, so only a member can see the readout); a member has "seen" the
message when their watermark ≥ the message id. Because watermarks are monotonic, the newest own
message is the only one that needs a readout — having seen it implies having seen every earlier one.

`ChatConversation` seeds the map once on open and keeps it live via `ReadStateChanged` (§3.2),
**max-merging** the seed against any event that landed between subscribe and load so a stale seed
can't rewind a watermark. The author, inactive/former members, and solo teams draw nothing.
`ChatMessageView` renders it Messenger-style: a single tick + "Sent" until anyone reads, then a
compact avatar stack (capped at 3, overflow "+N") with a names tooltip, becoming a green
double-tick + "Seen by all" once every other active member is caught up.

---

## 6. UI / UX

### 6.1 Global chat dock (all authenticated pages)

Rendered by both `MainLayout` and `TeamLayout` (gated inside the component on `authenticated && ≥1
claimed team`, §1), so a single dock is present on **every** authenticated page. Transient UI state
— is-open and the selected team — lives in a circuit-scoped `ChatUiState` service (§7), so
navigating between a `MainLayout` page and a `TeamLayout` page (which swaps the layout and rebuilds
the dock) restores the same open/selected state instantly; the conversation re-fetches in-process.
(Caveat: this survives only in-circuit navigation. The app's existing `forceLoad` hops — the nav
team-switcher's `SwitchTeam`, the claim-gate and login redirects — reload the page, start a fresh
circuit, and reset the dock to closed/default. Accepted for v1.)

```
                                        ┌───────────────────────────────┐
                                        │ 💬 Chats                   ✕  │
                                        ├───────────┬───────────────────┤
                                        │ Teams     │ Alfa Team    ● 3  │
                                        │ ▸Alfa   3 │ — Today —         │
                                        │  Beta   · │ 🦁 Alice   10:02  │
                                        │  Cveta  1 │   morning! game?  │
                                        │           │     👍 2  😂 1    │
                                        │           │ 🐺 Bob     10:03  │
                                        │           │   @Alice 12:30?   │
                                        │           │ 🚫 message deleted│
                                        │           │ Alice is typing…  │
                                        │           ├───────────────────┤
                                        │           │ [msg…]   😊    ➤  │
                                        └───────────┴───────────────────┘
                                 ┌─────┐
                                 │ 💬 4│   ← launcher (TOTAL unread), bottom-right
                                 └─────┘
```

- **Launcher**: fixed bottom-right floating button showing **total** unread across all the user's
  teams; toggles the dock. Sits clear of the live-game pill / footer.
- **Team switcher rail**: the user's claimed teams, each with its own unread badge and a small
  online dot; selecting one makes it the active conversation. On a `/team/{code}/*` page the dock
  **defaults its selection to that team**; elsewhere it restores the last-selected team (from
  `ChatUiState`) or the first team with unread.
- **Conversation** (`ChatConversation`, one per selected team): header shows the team name, online
  count (intersect that team's roster with `PresenceTracker.IsOnline`, exactly as
  `TeamLayout.GetOnlinePlayers()` already does — `PresenceTracker` tracks users globally, not per
  team), and close. Message list grouped by **local** day (`TimeZoneService`, per the §2 timezone
  policy); consecutive messages from the same sender within a
  short window are visually grouped (avatar shown once); avatar + name via the sender's `Player`;
  own messages subtly distinguished; reaction chips beneath each message. Composer = textarea +
  emoji-picker button + send; Enter sends.
- **No 🔔 notification toggle in v1** — browser notifications are deferred (§10); it returns with
  the app-wide notifications feature.
- Follows app conventions: Bootstrap, Bootstrap Icons, dark-mode aware (uses `bg-body`,
  `text-muted`, etc., like the rest of the UI).

### 6.2 Mobile

- Launcher sits above the footer; the dock becomes a near-full-width bottom sheet. The team
  switcher rail collapses to a top row of team chips (with unread badges); tapping a chip switches
  the conversation.
- All read/react/mention/type flows work on touch (react via tap, mention via the `@`
  autocomplete). No image upload in v1 regardless of platform.
- Soft keyboards have no Shift+Enter, so on touch devices **Enter inserts a newline** and the
  **send button** is the send affordance (standard mobile-chat behavior); Enter-sends (§4.1)
  applies to physical keyboards.

---

## 7. Services, DI & File Map

| File | Kind | Responsibility |
|------|------|----------------|
| `Data/Entities/ChatMessage.cs` `ChatMessageReaction.cs` `ChatReadState.cs` | Entities | §2 |
| `Data/AppDbContext.cs` (edit) | — | Add 3 `DbSet`s + `OnModelCreating` config/indexes |
| `Migrations/*_AddChat.cs` | Migration | Create the tables |
| `Services/ChatService.cs` | Scoped | All DB ops: send, delete, react/unreact, load page, mark read, unread counts (`GetUnreadCountsAsync`), seen-by watermarks (`GetReadWatermarksAsync`, §5.5), set typing. Every op takes an **explicit `teamId`** and re-verifies membership (§8) — there is no ambient "current team". Raises `ChatNotifier` events; enforces author-only delete |
| `Services/ChatNotifier.cs` | Singleton | In-process events (§3.2) + ephemeral typing state (§3.3) + per-user send throttle (§4.1) |
| `Services/ChatUiState.cs` | Scoped | Per-circuit dock UI state — `IsOpen`, `SelectedTeamId`, and the per-team unread cache with a `Changed` event (maintained by the dock; `TeamLayout`/`Home` badges subscribe, §5.2) — so the dock survives `MainLayout`↔`TeamLayout` swaps (§6.1) |
| `Components/Chat/ChatDock.razor` | Component | The **global** launcher + team-switcher shell; resolves the user's claimed teams via `GetMembershipOverviewForUserAsync`; subscribes to `ChatNotifier` once for all teams; owns selection, the total-unread launcher badge, and raises banners (§5.4); loads `chat.js` |
| `Components/Chat/ChatConversation.razor` | Component | One selected team's view: message list, pagination, composer, typing, read semantics. Parameterized by `teamId` + the user's `Player` for that team |
| `Components/Chat/ChatMessageView.razor` | Component | One message: segmented text (text/mention/emoji), reactions, ⋯ options menu (author-only edit/delete, §4.6), inline edit mode, tombstone, and the "seen by" indicator on the sender's latest message (§5.5) |
| `Components/Chat/EmojiPicker.razor` | Component | Wraps the vendored `emoji-picker-element` for the composer (§4.2); the quick-react row is a small static set of common emoji |
| `wwwroot/lib/emoji-picker-element/*` | Vendored lib | Self-hosted emoji-picker Web Component + its data (§4.2); omitted if the curated-array fallback is used instead |
| `Components/Chat/ChatDock.razor.css` | Scoped CSS | Dock/launcher/rail styling. The app styles components with scoped `.razor.css` (e.g. `Players.razor.css`); Bootstrap utilities + `bg-body`/`text-muted` cover most of it, so this holds only positioning and chat-specific bits |
| `Components/_Imports.razor` (edit) | — | Add `@using Fotbalek.Web.Components.Chat` — that namespace is not wildcard-imported, so `<ChatDock>` would not resolve from `MainLayout`/`TeamLayout` without it |
| `Components/Layout/MainLayout.razor` (edit) | — | Render `<ChatDock>` (gated inside the component to authenticated users with ≥1 claimed team) — covers Home, account, create, join, claim, etc. |
| `Components/Layout/TeamLayout.razor` (edit) | — | Render `<ChatDock>` (hinting the current team as the default selection); add secondary unread badges to the nav team switcher, rendered from `ChatUiState`'s cache + its `Changed` event (§5.2) |
| `Components/Pages/Home.razor` (edit) | — | Per-team unread badges on "Your Teams", rendered from `ChatUiState`'s cache + its `Changed` event (§5.2). No banner wiring needed — the dock handles that |
| `wwwroot/js/chat.js` | JS | Tab-title unread (§5.3) + visibility/focus reporting via `DotNetObjectReference` (§5.1). **No `Notification` API / permission / `chatOptions` in v1** — those arrive with browser notifications later (§11). Loaded **on demand as an ES module** — `await JS.InvokeAsync<IJSObjectReference>("import", "/js/chat.js")` from `ChatDock` (init guarded so the `MutationObserver` is wired once), mirroring how `LiveGame.razor` imports `game.js`. So **no `<script>` tag is added to `App.razor`** — consistent with `game.js`, which is also module-imported rather than globally tagged |
| `Program.cs` (edit) | — | Register `ChatService` (scoped) + `ChatNotifier` (singleton) + `ChatUiState` (scoped) |

**DTOs** (server → component): `ChatMessageDto` (id, senderUserId, resolved name+avatarId,
body, createdAt, isDeleted, editedAt, reactions summary), `ChatReactionDto`, and the event
payloads in §3.2. Deleted messages never carry `Body` over the wire.

> Note (from memory): CSS/JS under `wwwroot` is fingerprinted by `MapStaticAssets` at build, so
> during preview verification, edits to `chat.js`/CSS require a preview **stop + start** to
> appear, and Blazor screenshots are flaky (verify styles via computed-style instead).

---

## 8. Security & Validation

- **Authorization on every write**, never assumed from the circuit: send/delete/react/typing
  all verify `TeamMembershipService.IsMemberAsync(userId, teamId)` (dedupe-cheap; the game hub
  does the same). Reads verify membership before returning history.
- **Author-only delete** enforced server-side (`SenderUserId == userId`).
- **XSS**: user text is untrusted and is **only** rendered through Blazor's auto-encoding —
  never `MarkupString`. Mentions and emoji are rendered as structured segments/components, not
  by building an HTML string. Bodies are length-clamped server-side.
- **No cross-team leakage**: `joinFloorId` + `TeamId` filters mean a member can never query
  messages from before they joined or from another team.
- **Multi-team dock, no ambient team**: because one dock acts on *any* of the user's teams, every
  server op carries an **explicit `teamId`** and independently re-verifies membership for that team
  (and, for send, applies that team's join floor + the throttle). The dock never grants access
  implied by "the page I'm on" — a message to team B composed from team A's page is authorized
  against team B.

---

## 9. Constants / Config (initial, tunable)

| Constant | Value | Notes |
|----------|-------|-------|
| Max message length | 2000 chars | Server-clamped |
| History page size | 50 | Initial + scroll-back |
| Send throttle | ~5 msgs / 5 s per user | In-memory soft limit |
| Typing "typing" refresh | ≤ every 3 s while typing | Client → server |
| Typing server expiry | 6 s | Auto-clear if no refresh |
| Reaction emoji max length | 32 chars | Allows ZWJ/skin-tone sequences |
| Banner body preview | ~120 chars | Truncated (in-app banner) |
| In-app banner auto-dismiss | ~6 s | Bootstrap toast; manual dismiss also |

A `Constants.Chat` block (mirroring `Constants.Seasons`, etc.) is a reasonable home for these.

---

## 10. Out of Scope (v1)

- **Image / file / meme upload** (deferred; would need a storage-hosting decision — §11).
- ~~**Editing** messages~~ _(added 2026-07-10 — author-only inline editing, see §4.6)._
- **Threads / replies**, message search, pinning.
- **Captain/admin moderation** (author-delete-only settled, §12).
- **Cross-team or DM chat** — strictly per-team, members-only.
- ~~**Read receipts** ("seen by")~~ _(added 2026-07-12 — on the sender's latest own message, derived from read watermarks; see §5.5)._ Rich presence beyond the existing online dot remains out of scope.
- **Browser / OS notifications** — the OS `Notification` API (desktop/mobile alerts, permission
  prompts, `tag`-coalesced replace-on-repeat, click-to-focus) is **not in v1**. It belongs with a
  future **app-wide notifications** feature (§11), not a chat-only bolt-on. v1's entire signal
  story is in-app: banner + badges + tab title (no sound).
- **Chat where there's no user/team context** — the dock (and therefore all in-app signals) is
  absent only where it should be: anonymous pages (login/register), admin-only sessions, and for a
  user with **no claimed team yet**. Everywhere an authenticated member with a team goes, the dock,
  banners, badges, and tab title follow — the earlier "team-pages-only" gap is gone.
- **Notification sound** — no audio ping in v1; v1 signals are visual only (in-app banner +
  badges + tab title). Deferred, not dropped (§11).
- **Multi-instance scale-out** — the in-memory `ChatNotifier` + typing state assume a single
  server (same caveat as the live game); persisted messages survive restarts, but a restart
  drops transient typing and any in-flight subscriptions reconnect via the circuit. Scale-out
  would need a backplane (Redis / Azure SignalR) — not needed for ~team-sized usage.
- **Automated tests** — the solution has no test project today; `ChatService` is written so its
  logic is testable later without refactoring.

---

## 11. Future Extensions (kept compatible)

1. **Image / meme upload** — add an attachment table + blob/static hosting; the `ChatMessage`
   render path already segments content, so an image segment slots in.
2. **Notification sound** — an optional audio ping on notify (bundled asset or WebAudio), gated by
   a `fotbalek.chatOptions.sound` flag; deferred from v1 (§10). (Full emoji breadth already ships
   in v1 via `emoji-picker-element` — §4.2 — so no separate "emoji library" step is needed.)
3. **Denormalized unread counters** — cache a per-(user,team) unread int updated on post/read to
   avoid the count query entirely if team/message counts ever grow.
4. **Captain moderation** — the delete path already exists; relaxing the `SenderUserId` check to
   also allow `Team.CaptainUserId` is a one-line change if wanted later.
5. **Scale-out backplane** — replace `ChatNotifier`'s in-process events with Azure SignalR /
   Redis pub-sub; `ChatService`'s "write then notify" seam stays the same.
6. **Mention notifications when fully idle** — richer routing (e.g. email) off the same mention
   detection.
7. **Browser / OS notifications** — the `Notification` API (permission on a user gesture,
   `tag`-coalesced desktop/mobile alerts, click-to-focus), its 🔔 per-browser toggle and
   `fotbalek.chatOptions` (mirroring `fotbalek.playerOptions`). Deferred from v1 (§10); best
   delivered as part of an **app-wide** notifications system (chat, live-game invites, …) rather
   than chat-only. The `MessagePosted` event and mention detection are the ready hooks — this
   pairs naturally with item 6.

---

## 12. Decision Log

Settled with the user (2026-07-08):

| Topic | Decision |
|-------|----------|
| Placement | **Single global chat dock** on all authenticated pages (rendered by `MainLayout` + `TeamLayout`), with an in-panel team switcher — not a per-team-page panel, not a dedicated page (revised 2026-07-10) |
| Chat scope / access | The dock lists **only teams where the user has claimed a `Player`** (2026-07-10); unclaimed/transient memberships appear once claimed |
| v1 features | Text + emoji picker, **emoji reactions**, **@mentions**, **typing indicator** — **no** image upload |
| Unread / notify signals | Launcher **total-unread** badge + per-team badges (dock switcher, nav switcher, Home) + **tab title** + **in-app banner** — all live on **every authenticated page** (the dock is global). **No browser/OS notification and no sound** in v1 |
| Cross-team notify | A message in any team that isn't the dock's currently open+focused conversation raises an in-app banner (anywhere the user is), on top of the per-team badges (§5.4) |
| Browser / OS notifications | **Deferred to a future app-wide notifications feature** — not in chat v1 (settled 2026-07-10; §10/§11) |
| Notify across tabs | Each team-page tab shows its own in-app banner; no elected "primary" tab. OS-notification cross-tab dedup is deferred with browser notifications (§10/§11) |
| Notification sound | **None in v1** — visual-only (§10); deferred to future (§11) |
| Emoji picker | **Reuse a self-hostable library** (recommended `emoji-picker-element`) for the full Unicode set; curated static array only as a fallback (§4.2) |
| Retention | **Keep all history forever**; the UI lazy-loads it via **infinite scroll** (§4.7) — no capping/archival |
| Moderation | **Authors delete their own messages only**; no captain override in v1 |
| Editing (2026-07-10) | **Author-only inline editing allowed** (supersedes the original delete-and-resend-only stance): same length rules as send, `EditedAt` + "(edited)" marker, no banner/unread impact (§4.6) |
| Message actions placement (2026-07-10) | Edit + delete live under a **⋯ options menu** at the end of the action row — only quick-react emoji stay inline (§4.6) |
| Real-time transport | **In-process `ChatNotifier` + Blazor circuit** (PresenceTracker pattern) — **no** new SignalR hub (recommended by design; chat is not a frame loop) |
| Persistence | **DB-backed** (deliberately unlike the ephemeral live game) — history must survive |
| History visibility | Since **`TeamMembership.JoinedAt`**, implemented as a message-id floor |
| Unread model | Per-(user,team) `LastReadMessageId` watermark; unread = messages with `Id >` watermark |
| Identity/display | Sender name+avatar resolved from the sender's claimed `Player`, live at render (same as live game) |
| Read trigger | Mark read when the dock is **open**, the team is the **selected conversation**, and the tab is **focused/visible**; scrolling-into-view not required (§5.1) |
| @mention matching | **Longest-match against the team roster**; names containing spaces accepted as a known limitation (§4.4) |
| Rate limit / length | **~5 msgs / 5 s per user and a 2000-char cap** confirmed adequate for the office context (§9) |

---

## 13. Open Questions

_Resolved 2026-07-08 (earlier batch, now in §12): read trigger = open + focused; @mention
longest-match with spaces accepted; throttle ~5/5 s + 2000-char cap confirmed._

_Resolved 2026-07-08 (second batch, now in §12):_

1. **Notification dedupe across tabs** → each team-page tab shows its own in-app banner for teams
   it isn't viewing; no "primary" tab elected (§5.4). _(Superseded 2026-07-10: **browser / OS
   notifications are deferred** to a future app-wide notifications feature — §10/§11 — so the OS
   `tag` cross-tab dedup question no longer applies to chat v1. The in-app banner needs no
   permission.)_
2. **Sound** → **no sound in v1**; visual notifications only (§10), deferred to future (§11).
3. **Emoji picker breadth** → **reuse a self-hostable library** (recommended `emoji-picker-element`)
   for the full Unicode set; curated static array only as a fallback (§4.2).
4. **Retention** → **keep all history forever**; the UI lazy-loads it via the §4.7 infinite scroll.

_Updated 2026-07-10: **browser / OS notifications deferred** to a future app-wide notifications
feature (§10/§11); chat v1 keeps only in-app signals — banner + badges + tab title, no sound._

_Updated 2026-07-10: **placement changed to a single global chat dock** on all authenticated pages
(in-panel team switcher; lists only claimed-player teams). This supersedes the earlier
team-pages-only placement and removes the "signals outside team pages" limitation (§1/§6/§10/§12)._

**No open questions remain.**
