# Notifications

**Status**: **Draft for discussion — nothing implemented.** Drafted 2026-07-29; revised the same
day three times — to add **per-team preferences** (§8), replacing the original "no preferences in
v1" stance; to move the bell **from a floating launcher into the team navbar** (§9), where the
convention puts it; and to close out every open question, which changed the read model to a
**two-tier `SeenAt`/`ReadAt`** (§7).

**Revision 4 (2026-07-29) — verification pass.** Every claim about existing code was re-read
against `src/` line by line rather than from memory, which turned up **eight things that were
wrong**, not merely thin: the ladder tie-breaks (§6.1), the all-time snapshot going stale on a
seasonal match (§6.2), the read-clearing hook sitting on the wrong method (§7.3), a third mention
matcher nobody had counted (§5.2), a save-ordering hazard that would have produced ghost rows
(§4.1/§5.3), the panel's open state being invisible to the code that has to react to it
(§7.2/§9.3), and two files named that cannot host what was assigned to them (§8.5/§9.1). Four
design forks were settled with the user in the same pass. §17 is now the verification log: what
was checked, what was wrong, and what is still a bet.

**Revision 5 (2026-07-29) — independent re-audit.** Every existing-code claim was re-verified by
four independent parallel audits. The revision-4 findings held; the audit surfaced **five real
gaps**: a second season-close path (`EndSeasonNowCommand`) that would have ended seasons silently
(§5.5); a third seasonal-ELO writer (`SeasonLadderReplay`, reached from `UpdateSeasonEndsAtCommand`)
that would leave the seasonal snapshot stale (§6.3); the lazy start-announcement colliding with the
lazy close when a season runs its whole course unvisited (§5.4); the `StartAnnouncedAt` backfill
permanently muting seasons scheduled to start *after* deployment (§3.6); and the aftermath handler
loading a match row that no longer exists on the delete path (§6.3 — scope now rides the command).
Plus smaller corrections, all logged in §17.4: the aftermath bridge belongs in Application beside
its command, not Web; the actor-exclusion rule now says which writes are actor-less (§4.2); lead
rows are system rows, not actor rows (§9.1); a fourth mention matcher (the composer autocomplete)
is counted and dismissed (§5.2); and assorted precision fixes.

**Planned code map** (nothing exists yet):

| Project | Files |
|---|---|
| `Fotbalek.SharedKernel` | `NotificationType.cs`, `NotificationCategory.cs`, `NotificationChannel.cs`, `NotificationCategories.cs` (type→category map); `Constants.Notifications` block |
| `Fotbalek.Domain/Entities` | `Notification.cs`, `NotificationPreference.cs`, `LadderLeader.cs`; **edit** `Season.cs` (+`StartAnnouncedAt`) |
| `Fotbalek.Infrastructure/Persistence` | **edit** `AppDbContext` (3 `DbSet`s + config + indexes); one migration (incl. the backfill guards, §3.6) |
| `Fotbalek.Contracts/Notifications` | `NotificationDto.cs`, `NotificationPreferenceDto.cs` |
| `Fotbalek.Application/Common/Abstractions` | `INotificationWriter.cs`; **edit** `IAppDbContext.cs` (+`Notifications`, `NotificationPreferences`, `LadderLeaders` — easy to forget, and nothing compiles without it) |
| `Fotbalek.Application/Features/Notifications` | `NotificationWriter.cs`, `NotificationEvents.cs`, `GetNotificationsQuery.cs`, `GetUnseenNotificationCountQuery.cs`, `GetUnseenNotificationCountsByTeamQuery.cs`, `MarkNotificationsSeenCommand.cs`, `MarkNotificationReadCommand.cs`, `MarkAllNotificationsReadCommand.cs`, `GetNotificationPreferencesQuery.cs`, `SetNotificationPreferenceCommand.cs`, `NotificationRecipients.cs`, `EvaluateMatchAftermathCommand.cs` (+ its post-commit `MatchRecordedBridge`, §6.3), `RefreshLadderLeadersCommand.cs` (§6.3), `MatchMilestones.cs`, `AnnounceStartedSeasonsCommand.cs` |
| `Fotbalek.Application/Features/Stats` | new `Queries/LadderLeaders.cs` (the shared ranking rules, §6.1) and `Rivalries/NemesisRule.cs` (extracted from `NemesisStat`, §6.5); **edit** all six ranking queries + `NemesisStat` to call them |
| `Fotbalek.Application/Features/Chat` | **edit** `SendChatMessageCommand`, `EditChatMessageCommand`, `ToggleChatReactionCommand`, `ChatReadStateAdvancer` (**not** `MarkChatReadCommand` — §7.3); new `MentionScanner.cs` (§5.2) |
| `Fotbalek.Application/Features/Matches` | **edit** `CreateMatchCommand` (raise event), `DeleteMatchCommand` (cleanup + silent re-evaluate) |
| `Fotbalek.Application/Features/Seasons` | **edit** `CreateSeasonCommand`, `CloseSeasonCommand`, `EndSeasonNowCommand` (the second close path, §5.5), `SeasonCloseProcedure` (return the frozen result, §5.5), `UpdateSeasonEndsAtCommand` (silent ladder refresh after the tail unassign, §6.3), `DeleteSeasonCommand`, `GetDueSeasonIdsQuery` → `GetTeamSeasonHooksQuery` (§5.4) |
| `Fotbalek.Application/Features/Players` | **edit** `DeactivatePlayerCommand`, `ReactivatePlayerCommand` (silent ladder refresh — §6.3) |
| `Fotbalek.Web/Realtime` | `NotificationNotifier.cs`, `NotificationEventBridge.cs` (the created/read-state → notifier handlers only; the match-aftermath bridge is Application's, §6.3), `NotificationUiState.cs`; **edit** `ChatUiState.cs` (+`RequestOpen`, §5.2) |
| `Fotbalek.Web/Components/Notifications` | `NotificationBell.razor` (the navbar dropdown, §9.1), `NotificationRow.razor`, `NotificationSettings.razor` (the Account panel, §8.5) |
| `Fotbalek.Web/Services` | `NotificationPresentation.cs` (all wording, icons and targets — §10) |
| `Fotbalek.Web/Components/Chat` | **edit** `ChatMessageView.razor` (pills from the shared scanner), `ChatDock.razor` (subscribe to `RequestOpen`) |
| `Fotbalek.Web` | **edit** `TeamLayout.razor` (render the bell in the navbar actions), `Home.razor` (per-team badge, §9.4), `Account.razor`, `CurrentTeamProvider.cs`, `_Imports.razor`, `Program.cs`, `wwwroot/js/app.js` (scroll helper — **not** `ui.js`, §8.5), `Styles/components/team-navbar.css` (panel sizing — **not** `nav.css`, §9.1) |

---

## 1. Description

A persistent, per-user notification feed: the app records the things that happened *to you*
(a match you played in, a mention, a reaction, a season milestone, a ladder lead won or lost),
each row carries its own read/unread state, and clicking one takes you to the thing it is about.
Surfaced through a **bell in the team navbar**, beside the account menu, opening an anchored
dropdown panel — where every app of this shape puts it (§9). What lands in the feed is
**configured per team**: all of it from one team, only the ladder changes from another (§8).

**Design principle** — this is the *record*, chat's banners are the *cue*. Chat already has a
complete live-signal story (unread badges, in-app banners, tab title — chat.md §5) and it stays
exactly as it is. Notifications are DB-backed, survive restarts, and answer "what did I miss
while I was away, and what have I already looked at?" The two features overlap deliberately at
exactly one point: a mention raises chat's transient banner *and* leaves a permanent row in the
bell (§5.2).

**Scope anchors in the existing model:**
- **A notification belongs to an account, not to a team.** `Notification.UserId` is the recipient
  and the write fans out one row per person (§3.1); the feed is read owner-scoped and spans every
  team you are in. `TeamId` on the row is a *label, a navigation target and the axis preferences
  filter on* — it never partitions the feed. One person, one inbox.
- Every trigger in v1 nonetheless *happens* inside a team, which is what makes per-team
  preferences (§8) the natural granularity: the team decides what gets created, the account sees
  all of what did.
- **Recipients are users with a claimed `Player` in that team.** Same rule as the chat dock
  (chat.md §1): a membership without a claimed player is a transient state, and every v1 trigger
  is about a player anyway. A member who has not claimed a player gets nothing until they claim.
- Actor display name + avatar == the actor's claimed `Player` in that team, resolved at read
  time — same rule as chat and the live game.
- Existing pattern reuse, top to bottom: writes happen inside the acting command's transaction
  (§4.1), delivery rides the post-commit `IEventCollector` → Web bridge → in-process notifier →
  circuit (architecture.md §4.2/§4.4), and the UI mirrors `ChatDock`/`ChatUiState`.
- **v1 is entirely in-app.** Browser/OS notifications are designed in §13 as a separate phase,
  and slot into the preference model as a second channel bit (§8.2).

---

## 2. Feasibility notes (what the codebase makes cheap, and what it doesn't)

Checked before designing; these shape several decisions below.

| Idea | Verdict |
|---|---|
| "Someone recorded a match with you" | **Cheap.** `CreateMatchCommandHandler` already has the four player ids and the team; recipients are the claimed users among them, minus the actor. |
| "You were mentioned in chat" | **Cheap, but needs a code move.** Mention matching lives in the *Web* components today — and in **two** places, not one: `ChatMessageView.ComputeSegments` (the pills) and `ChatDock.MentionsMe` (the banner wording, a plain substring test). The server must match too, which makes three; hence the shared `MentionScanner` (§5.2). |
| "Someone reacted to your message" | **Cheap.** `ToggleChatReactionCommand` knows the message, its author and the reactor. Needs toggle-spam protection (§5.3). |
| "Season ended" | **Cheap.** `CloseSeasonCommand` → `SeasonCloseProcedure` already freezes final ranks and awards; the notifications write in the same transaction. |
| "Season started" | **Not free — nothing runs at `StartsAt`.** The app has **no background service or scheduler** (verified in `Program.cs`: migrations at startup, nothing hosted). Seasons can be created ahead of time and simply *become* active when `StartsAt` passes. Solved by lazily materializing the announcement on the existing team-page hook, exactly like the lazy season close (§5.4). |
| "Position changed" | **Feasible, and the interesting part of this spec.** *Live* rank is never stored — it is computed on the fly, in memory, by six queries (§6.1). (A *closed* season's rank **is** stored, as `SeasonPlayerResult.FinalRank`, which is what §5.5 reads; that path needs no snapshot.) Detecting a live change therefore needs either a before/after diff or a persisted leader snapshot; this spec uses a snapshot (§6). |
| Reusing the ranking rules | **Cheaper than it looks, but not free.** Three of the six ranking queries have **no final tie-break**, so their #1 is not deterministic on a tie (§6.1). The shared helper has to add one, and those three queries adopt it — a required prerequisite, not a nicety: without it a tie flip-flops the snapshot and fires a spurious took/lost pair on *every* match. |
| Per-team preferences | **Cheap.** Sparse override rows keyed `(UserId, TeamId, Category)`, mirroring `ChatReadState`'s shape and cascade choices exactly. Enforcement is one extra query on the write path (§8.3). |
| Browser notifications | **Possible but a real project.** Blazor Server holds a circuit only while a tab is open, so reaching a closed tab or a phone means **Web Push**: VAPID keys, a subscription table, service-worker `push`/`notificationclick` handlers, a push library (license check — the repo is deliberate about that), and an outbound sender that must not block a request. The PWA scaffold is already in place, which is the prerequisite for iOS. Designed in §13, deferred out of v1. |

---

## 3. Data model

Three new entities plus one column on `Season`. `int` keys and `DateTimeOffset` timestamps in
**UTC**, converted to local only at the UI boundary via `TimeZoneService` (repo timezone policy).

### 3.1 `Notification`

One row per **recipient** — a fan-out write, not a shared row with a join table. At this scale
(a handful of recipients per event) the duplication is trivial and it makes both the unread count
and the read flag a single-row concern.

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `int` PK | Identity — **monotonic**; the ordering key and the pagination cursor (same reasoning as `ChatMessage.Id`). |
| `UserId` | `int` FK → `AppUser` | Recipient. `OnDelete(Restrict)` — the repo's convention for *content-bearing* user FKs (`ChatMessage.SenderUserId`, `ChatMessageReaction.UserId`; per-user **state** rows like `ChatReadState` cascade instead, which is what §3.3 mirrors), and it keeps a single cascade path into this table. |
| `TeamId` | `int` FK → `Team` | `OnDelete(Cascade)` — a deleted team takes its notifications, like its chat. |
| `Type` | `NotificationType` (enum → `int`) | §3.2. Lives in SharedKernel because both the entity and the DTO use it (architecture.md §7 step 2). |
| `CreatedAt` | `DateTimeOffset` | `UtcNow` at insert. |
| `SeenAt` | `DateTimeOffset?` | Null = never shown to the user. **Drives the badge** (§7.1). |
| `ReadAt` | `DateTimeOffset?` | Null = unread. **Drives the row's own unread styling** (§7.1). |
| `ActorPlayerId` | `int?` FK → `Player` | Who caused it, as their player in this team. Null for system events (season start/end, milestones). Name + avatar resolved at read time. |
| `SubjectPlayerId` | `int?` FK → `Player` | The *other* player the row is about: the duo partner for a pair lead, the player who took the lead from you. |
| `MatchId` | `int?` FK → `Match` | |
| `SeasonId` | `int?` FK → `Season` | |
| `ChatMessageId` | `int?` FK → `ChatMessage` | |
| `Category` | `string?` maxlen 16 | For ladder/award rows: reuses `Constants.Seasons.AwardCategories` (`Player` / `Goalkeeper` / `Attacker` / `Pair`) — the four award categories and the four ladders the user picked are the same four. Not to be confused with `NotificationCategory` (§8.1), which is the *preference* grouping. |
| `Value` | `int?` | The one number the type needs: final rank, award rank, streak length, match count, new peak ELO. |
| `Emoji` | `string?` maxlen `Constants.Chat.MaxReactionEmojiLength` (32) | Reaction rows only; reuse the constant, don't restate the number. Display-only, so unlike `ChatMessageReaction.Emoji` it needs **no binary collation** — nothing compares or uniquely indexes it. |
| `DedupKey` | `string` maxlen 128, required | Idempotency (§4.3). |

**Cascade shape (important).** `TeamId` cascades, so every other FK — `MatchId`, `SeasonId`,
`ChatMessageId`, `ActorPlayerId`, `SubjectPlayerId` — must not: `Team → Match →
Notification` alongside `Team → Notification` would be two delete paths from one root, which SQL
Server rejects (the same hazard `ChatMessage` documents). Spell that as
**`DeleteBehavior.Restrict`**, which is what the repo already writes for every non-cascading
user/player FK (`ChatMessage.SenderUserId`, `MatchPlayer.PlayerId`, `SeasonAward.PlayerId`) — it
emits the same `NO ACTION` constraint and also stops EF's change tracker from quietly trying to
fix things up. Consequence: the two **hard**-delete paths in the app must clean up explicitly —

- `DeleteMatchCommand`: `db.Notifications.Where(n => n.MatchId == id).ExecuteDeleteAsync()`
  (also the right behaviour on its own: a deleted match must not leave a link to a 404).
- `DeleteSeasonCommand`: same for `SeasonId`.

Players are soft-deactivated and chat messages soft-deleted, so neither needs cleanup — verified:
the only `Remove`/`ExecuteDelete` calls in the whole Application layer are `Match`, `Season`,
`SeasonPlayer` and `ChatMessageReaction`. Deleting a whole `Team` has no code path today; if one is
ever added, delete its notifications first rather than trusting cascade ordering against the
restricted FKs.

One consequence to accept: a `ChatReaction` or `ChatMention` row survives its message being
deleted, because that delete is a tombstone. The row stays in the feed and still opens the dock —
where the message reads "message deleted". That is honest (you *were* reacted to at the time) and
cheaper than hunting rows on every delete.

Indexes:
- `(UserId, Id)` **descending on `Id`** — the feed page and the cursor.
- Filtered `(UserId) INCLUDE (TeamId) WHERE SeenAt IS NULL` — the badge count, which runs on
  every bell render, and the per-team breakdown Home needs (§9.4). `ReadAt` needs no index: it is
  only ever read on rows the feed has already loaded.
- Unique `(UserId, DedupKey)` — the idempotency guard.
- `(MatchId)`, `(SeasonId)`, `(ChatMessageId)` — the cleanup deletes and the read-sync in §7.3.

### 3.2 `NotificationType` (v1)

| Value | Preference category (§8.1) | Recipient | Carries |
|---|---|---|---|
| `MatchRecorded` | `Matches` | the other three players in the match | `MatchId`, `ActorPlayerId` |
| `ChatMention` | `Chat` | each mentioned player | `ChatMessageId`, `ActorPlayerId` |
| `ChatReaction` | `Chat` | the message author | `ChatMessageId`, `ActorPlayerId`, `Emoji` |
| `SeasonStarted` | `Seasons` | every claimed member | `SeasonId` |
| `SeasonEnded` | `Seasons` | every claimed member | `SeasonId`, `Value` = your `FinalRank` (null if you didn't play / were inactive at close) |
| `SeasonAward` | `Seasons` | each award winner | `SeasonId`, `Category`, `Value` = award rank (1–3), `SubjectPlayerId` = partner for `Pair` |
| `LadderLeadTaken` | `Rankings` | the new leader (both members for `Pair`) | `Category`, `MatchId`, `SeasonId?` = scope, `SubjectPlayerId` = partner for `Pair` |
| `LadderLeadLost` | `Rankings` | the previous leader (both members for `Pair`) | `Category`, `MatchId`, `SeasonId?`, `SubjectPlayerId` = who took it |
| `PeakElo` | `Milestones` | the player | `MatchId`, `Value` = new peak |
| `WinStreak` | `Milestones` | the player | `MatchId`, `Value` = streak length |
| `MatchMilestone` | `Milestones` | the player | `MatchId`, `Value` = total matches played |
| `NemesisBeaten` | `Milestones` | the player | `MatchId`, `SubjectPlayerId` = the nemesis |

The enum is the only thing the Application layer knows about presentation — **no wording, no
icons, no URLs** live in Application or in the entity (repo convention, commit `f62ff6f`
"no ui texts in app layer"; the `StatPresentation` precedent). See §10.

### 3.3 `NotificationPreference`

Sparse **overrides**: a row exists only where the user has changed something. Absence = the
default (§8.2). Shape and cascade choices mirror `ChatReadState`, which is the closest existing
analogue (per-user, per-team, one row).

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `int` PK | |
| `UserId` | `int` FK → `AppUser` | `OnDelete(Cascade)` — a user's own settings. |
| `TeamId` | `int?` FK → `Team` | `OnDelete(Cascade)`. **Null is reserved for a future global-defaults tier; v1 never writes null** (§16, deferred). |
| `Category` | `NotificationCategory` (enum → `int`) | §8.1. |
| `Channels` | `NotificationChannel` (flags → `int`) | `None = 0, InApp = 1, Push = 2`. v1 reads and writes the `InApp` bit only; `Push` is what phase 2 turns on without a migration (§13). |
| `UpdatedAt` | `DateTimeOffset` | |

Unique index `(UserId, TeamId, Category)` — SQL Server's unique index permits a single NULL, so
the future global tier fits without changing it. Plus `(UserId)` for the settings page load.

Two cascade FKs into this table from *different* roots (`AppUser` and `Team`) is fine and already
precedented — `ChatReadState` and `TeamMembership` both do it. The rejected pattern is two paths
from *one* root, which is why §3.1's subject FKs are `Restrict`.

### 3.4 `LadderLeader`

The persisted "who is currently #1" snapshot that makes lead-change detection a comparison
instead of a before/after double computation (§6).

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `int` PK | |
| `TeamId` | `int` FK → `Team` | `OnDelete(Cascade)`. |
| `SeasonId` | `int?` FK → `Season` | Scope. **Null = the all-time ladder.** `OnDelete(Restrict)` (cascade-diamond again, §3.1); `DeleteSeasonCommand` clears its rows. |
| `Category` | `string` maxlen 16 | `Player` / `Goalkeeper` / `Attacker` / `Pair`. |
| `PlayerId` | `int` FK → `Player`, `Restrict` | Current leader. For `Pair`, the lower player id. |
| `PartnerPlayerId` | `int?` FK → `Player`, `Restrict` | `Pair` only — the higher player id. |
| `EvaluatedAt` | `DateTimeOffset` | |

Unique index `(TeamId, SeasonId, Category)`. SQL Server's unique index allows a single NULL, so
this permits exactly one all-time row per team+category — which is what we want.

**Row absence means "never evaluated", and that is load-bearing** — it is what makes the very
first evaluation silent (§6.3, §3.6). Two consequences worth stating out loud rather than
discovering:

- **The first lead in a brand-new season is never announced.** A new season's ladders start empty,
  so the first seasonal match writes the snapshot silently and whoever tops the table after match
  one is not told. Accepted: after a single match "you're #1 in the season" is noise, not news.
  The same applies to a ladder that empties and refills.
- **A closed season's rows are frozen by construction.** No match can be added to a closed season,
  so nothing re-evaluates it; the rows sit there harmlessly and need no cleanup pass.

§15 lists the fix if the first-lead silence ever grates: make `PlayerId` nullable so
"evaluated, nobody eligible" becomes representable and distinct from "never evaluated".

### 3.5 `Season.StartAnnouncedAt`

`DateTimeOffset?` — null until the "season started" announcement has been made. Mirrors
`ClosedAt`: a single-row guard, written under the existing season row lock, so concurrent page
loads can't double-announce **without relying on a unique-index violation as flow control**
(architecture.md §4.1: exceptions are not flow control). The `DedupKey` remains as a second net.

Needs its own index — the existing season index is `(TeamId, ClosedAt, EndsAt)`, which serves the
*due-close* lookup; the announcement lookup filters on different columns, so add
`(TeamId, StartAnnouncedAt, StartsAt)`.

### 3.6 Migration, backfill guards & DI

One EF migration adds the three tables and the column, applied by the existing startup
`db.Database.Migrate()`. It **must** include two backfills, or the feature announces the entire
history on the day it ships:

1. `UPDATE Seasons SET StartAnnouncedAt = CreatedAt WHERE StartAnnouncedAt IS NULL AND StartsAt <= SYSDATETIMEOFFSET()`
   — no "season started" notifications for seasons that started before the feature existed. The
   second predicate is not optional: without it the backfill also stamps seasons already created
   but **scheduled to start after deployment**, permanently muting their announcement — the exact
   rows the feature is supposed to catch. Hand-written as `migrationBuilder.Sql(...)` in the
   generated migration's `Up`, after the `AddColumn`; the scaffolder will not produce it.
2. Nothing seeds `LadderLeader`: the **first evaluation per (team, scope, category) writes the
   snapshot silently** (§6.3), which is the equivalent guard for ladder leads.

`NotificationPreference` needs no backfill — an empty table means everyone is on the defaults,
which is the intended starting state.

DI (`Program.cs`):
```csharp
builder.Services.AddSingleton<NotificationNotifier>();
builder.Services.AddScoped<NotificationUiState>();
```
Everything else registers through the existing `AddApplication()` assembly scan (which already
scans the Web assembly, so the new bridge handlers register — architecture.md §4.2).

---

## 4. How notifications are written

### 4.1 Inside the acting command's transaction

An `INotificationWriter` (Application, `Common/Abstractions`; implementation in
`Features/Notifications`) is injected into the handlers that trigger notifications. It filters
recipients by their preferences (§8.3), adds `Notification` rows to the tracked `IAppDbContext`,
and enqueues one `NotificationCreatedEvent` per surviving row on `IEventCollector`.

Why in-transaction rather than "react to the event afterwards": a notification for an action that
rolled back is a lie, and the writer has the actor, the entity ids and the recipients right
there. The *delivery* is still post-commit — `TransactionBehavior` flushes the collector only
after a successful commit (architecture.md §4.2), which is exactly the ghost-message problem
chat already solved.

```csharp
public interface INotificationWriter
{
    /// <summary>Queues one row per recipient that still wants this category in this team.
    /// Drops the actor's own id and any duplicate DedupKey. Async because it resolves
    /// preferences (§8.3). ADDS TO THE CHANGE TRACKER ONLY — the caller must SaveChanges.</summary>
    Task AddAsync(NotificationDraft draft, IEnumerable<int> recipientUserIds, CancellationToken ct);
}
```

**The ordering rule this creates, and why it is not optional.** `AddAsync` does two things: it adds
entities to the tracked context *and* it enqueues one event per row on `IEventCollector`. The
collector is flushed by `TransactionBehavior` after commit regardless of what the handler did in
between — so a handler that calls `AddAsync` and then never reaches a `SaveChangesAsync` publishes
events for rows that were never inserted. Every circuit of that user then renders a notification
whose row does not exist, and the badge count (recomputed from the DB, §7.2) disagrees with it on
the next refresh.

So: **`AddAsync` must be followed by a `SaveChangesAsync` on every path that reaches it**, and it
must be called only once the triggering write is known to have succeeded. Three of the four call
sites get this for free because they already save afterwards; `ToggleChatReactionCommandHandler`
does not, and is the one that needs care (§5.3). Note also that `ExecuteUpdate`/`ExecuteDelete` do
**not** count — they bypass the change tracker entirely, so a handler whose last statement is a
bulk operation still owes a `SaveChangesAsync`.

### 4.2 Recipient resolution

One shared helper (`Features/Notifications/NotificationRecipients`) so every trigger uses the
same rule:

- `ForPlayers(teamId, playerIds)` → the `UserId`s of those players where `UserId != null`.
- `ForTeam(teamId)` → every user with a claimed player in the team.

Both exclude the actor. Both are plain queries on `Players` — no membership join needed, since a
claimed player *is* the membership signal the rest of the app uses. Preference filtering happens
after this, inside the writer.

**"The actor" is a user id carried on the draft, and some writes deliberately have none.**
`NotificationDraft` carries an `int? ActorUserId` (normally `IUserContext.UserId`) that the
resolver and the writer exclude, alongside the display-only `ActorPlayerId` (§3.1). The two are
separable on purpose: the aftermath's drafts (§6 — ladder leads and milestones) set **neither**.
They are system rows, so the *recorder* still receives their own `LadderLeadTaken`, `PeakElo` and
`WinStreak` rows — recording a match is incidental to those, and self-recorded matches are the
common case. Only `MatchRecorded`, mentions and reactions treat the acting user as the actor.

### 4.3 Idempotency: `DedupKey`

Every draft composes a key; the unique `(UserId, DedupKey)` index is the backstop and the writer
also de-duplicates within the batch. Keys are anchored on the event that can only happen once:

| Type | Key |
|---|---|
| `MatchRecorded` | `match:{matchId}` |
| `ChatMention` | `mention:{messageId}` |
| `ChatReaction` | `reaction:{messageId}:{actorUserId}:{emoji}` |
| `SeasonStarted` / `SeasonEnded` | `season-started:{seasonId}` / `season-ended:{seasonId}` |
| `SeasonAward` | `award:{seasonId}:{category}:{rank}` |
| `LadderLeadTaken` / `LadderLeadLost` | `lead-taken:{scope}:{category}:{matchId}` / `lead-lost:…` |
| `PeakElo` / `WinStreak` / `MatchMilestone` / `NemesisBeaten` | `peak:{matchId}` / `streak:{matchId}` / `milestone:{matchId}` / `nemesis:{matchId}:{opponentPlayerId}` |

Anchoring the milestone keys on `matchId` (not on the value) is deliberate: a player who hits a
3-win streak twice in a season should be told twice, but a replayed evaluation of the *same*
match must not duplicate.

### 4.4 Delivery to circuits

Straight copy of the chat bridge (architecture.md §4.4, `ChatEventBridge`):

```
Application handler ──enqueue──▶ IEventCollector
                                      │  (TransactionBehavior, after commit)
                                      ▼
                      NotificationCreatedEvent (INotification)
                                      │
                       Web: NotificationEventBridge
                                      ▼
                    NotificationNotifier (singleton)
                                      │  event Created(userId, dto)
                                      │  event ReadStateChanged(userId)
                                      ▼
                    NotificationBell in every circuit of that user
```

`NotificationNotifier` carries `int UserId` on every event; subscribers ignore other users' —
same pattern as `ChatNotifier` (whose events key on `teamId`; the filtering-subscriber idea is the
shared part, not the key), same single-instance caveat (§14).

---

## 5. Trigger catalog — the simple five

### 5.1 `MatchRecorded`

- **Where**: `CreateMatchCommandHandler`, after `SaveChangesAsync` (the match needs its id).
- **Recipients**: the claimed users among the four players, minus the recorder. A player who
  recorded their own match gets nothing.
- **Target**: `/team/{code}/matches/{matchId}`.
- The row is *informational*, not a confirmation — the recorder already sees the result panel on
  New Match, and per `ToastService`'s own rule that case gets neither toast nor notification.

### 5.2 `ChatMention`

Mention matching moves to a shared, pure `MentionScanner` in
`Fotbalek.Application/Features/Chat`:

```csharp
public static IReadOnlyList<MentionSpan> Scan(string body, IReadOnlyList<RosterName> roster);
public readonly record struct MentionSpan(int Start, int Length, int PlayerId);
```

It implements the rule that exists today in `ChatMessageView.ComputeSegments`: after each `@`,
longest-match against the team roster, case-insensitive, names with spaces accepted (chat.md §4.4)
— and, faithfully, **no word-boundary requirement before the `@`** (`mail.foo@Alice` renders a
mention pill today and must keep matching the same way; tightening that rule is a separate
decision to make in both places at once, not a side effect of the move).
`ChatMessageView` is refactored to build its pills **from this scanner's spans** instead of its
own copy. That refactor is not optional: if the two matchers drift you get a highlighted mention
pill with no notification, or worse the reverse. Returning spans (not markup) keeps the split
clean — Application owns the *matching rule*, Web owns the markup. Web already references
Application for request types, so the type is reachable.

Two details the scanner has to inherit exactly, both verified in the current code:

- **The roster includes inactive players.** `ChatConversation` loads
  `GetTeamPlayersQuery(TeamId, IncludeInactive: true)` and sorts the names longest-first. The
  server-side load must match, or a mention of a deactivated player renders as a pill and produces
  no row. A deactivated player who still has a claimed user therefore *does* get notified — correct,
  they are still a person on the team.
- **Name → player id is unambiguous.** `PlayerRules.IsNameTakenAsync` enforces case-insensitive
  name uniqueness per team, which is what makes `MentionSpan.PlayerId` well-defined at all. Worth
  knowing that the scanner leans on that invariant.

**The third matcher.** `ChatDock.MentionsMe` decides the banner's wording with
`body.Contains("@" + playerName, OrdinalIgnoreCase)` — a bare substring test with no roster and no
longest-match rule. It diverges from the other two whenever **one roster name is a prefix of
another**: with both "Jan" and "Jan Novák" on the team, `@Jan Novák hi` matches the longer name in
the scanner and notifies only Jan Novák — while Jan's dock still pops a banner reading
"…mentioned you", because `@Jan` is a substring. Not exotic in a team that has a Tom and a Tomáš.

**v1 leaves it alone** and this spec records why: the banner is chat's surface, the test only picks
between two wordings of a message the user is being shown either way, and driving it from spans
would mean putting mention data on `ChatMessageDto`. It is listed in §15 as the tidy-up, and it is
the one place where "the matchers must not drift" is knowingly not enforced.

**And a fourth matcher, which is fine as it is.** The composer autocomplete
(`ChatConversation.ComputeMentionState`) is its own implementation again — prefix-search over the
roster, **active players only**, and it requires the `@` to sit at the start or after whitespace.
It *suggests* mentions rather than deciding them, so it does not have to agree with the scanner:
an inactive player can't be autocompleted, but a hand-typed mention of them still pills and
notifies (consistent with the roster rule above). Not a drift risk — recorded so nobody counts it
as one, or "unifies" it into the scanner by mistake.

- **Where**: `SendChatMessageCommandHandler` (roster load: one `Players` query on the team, which
  the handler can share with nothing else today — accepted cost, ~one small query per send) and
  `EditChatMessageCommandHandler`, where only **newly** added mentions notify (the
  `mention:{messageId}` dedup key makes that automatic).
- **Recipients**: mentioned players' claimed users, minus the sender. Self-mentions are dropped.
- **Target**: opens the **chat dock** on that team — not a URL. **This needs one small addition to
  `ChatUiState`.** Setting `IsOpen`/`SelectedTeamId` from the bell would change the state and render
  nothing: `ChatDock` is a *sibling* component, and Blazor only re-renders the component whose
  handler fired. `ChatUiState` today raises `Changed` from `SetUnreadCounts` and nowhere else, so
  nothing tells the dock to redraw. Add:

  ```csharp
  public event Action? OpenRequested;                 // ChatUiState
  public void RequestOpen(int teamId)
  {
      IsOpen = true;
      SelectedTeamId = teamId;
      OpenRequested?.Invoke();
  }
  ```

  `ChatDock` subscribes in `OnAfterRenderAsync(firstRender)` beside its other subscriptions,
  clears `_banners` and calls `InvokeAsync(StateHasChanged)`, and unsubscribes in `DisposeAsync`.
  (Its existing `OpenFromBanner` clears `_banners` too but skips the explicit `StateHasChanged` —
  it can, because it runs as the component's own `@onclick` handler and re-renders automatically;
  a handler invoked from another component's event cannot.)

  Jumping to the specific message is **not** supported: `ChatConversation` paginates from the
  newest message and has no seek-to-id path (chat.md §4.7). Listed as a future extension (§15).
- **Overlap with chat**: intentional and documented. The banner is the cue that vanishes; the
  bell row is the record that doesn't. See §7.3 for how the two read-states are kept in step.
  Note that muting the `Chat` category (§8) suppresses only the **bell row** — chat's own banner
  and unread badges are chat's feature and keep working. That is the right split: you mute the
  *log*, not the conversation.

### 5.3 `ChatReaction`

- **Where**: `ToggleChatReactionCommandHandler`, **only on the add half of the toggle**.
- **Recipients**: the message author, if it isn't the reactor. Never on a tombstoned message
  (already rejected server-side, chat.md §4.6).
- **Anti-spam**: the dedup key includes the emoji, so toggling the same emoji off and on does not
  re-notify. Removing a reaction does **not** delete the notification — you were told a true
  thing at the time. Aggregating ("3 people reacted to your message") is a future option (§15).
- **Target**: same as a mention — open the dock on that team.

**Where exactly, because this handler is the awkward one.** Its shape today is: decide add-or-remove
→ `SaveChangesAsync` inside a `try` that **swallows `DbUpdateException`** (the two-tabs race, where
the other toggle won) → load the summary → enqueue the chat event. That leaves no safe seam for a
naive `AddAsync` call:

- *Before* the save — the notification rows join the save that can throw. On the race path nothing
  is inserted, the `catch` swallows it, there is no later `SaveChangesAsync`, and the row is silently
  dropped while its event still flushes (§4.1). Worse, the race path is precisely the case where the
  reaction *already existed*, so no notification was warranted anyway.
- *After* the save with no guard — you cannot tell whether this call added or removed.

So: capture the branch in a local (`var added = existing == null;`), and after the try/catch do

```csharp
if (added && !saveFailed && authorUserId != userId)
{
    await writer.AddAsync(draft, [authorUserId], cancellationToken);
    await db.SaveChangesAsync(cancellationToken);   // §4.1 — AddAsync only tracks
}
```

with `saveFailed` set in the existing `catch`. Two `SaveChanges` on the add path, one transaction
(the behavior's), and the race path notifies nobody.

### 5.4 `SeasonStarted`

Two paths, because nothing runs at `StartsAt` (§2):

1. **Starts immediately** — `CreateSeasonCommandHandler` sees `StartsAt <= now`: notify every
   claimed member (minus the creator) in the same transaction and stamp `StartAnnouncedAt`.

   ⚠ **With one guard, or the feature's first act is nonsense.** `CreateSeasonCommand` also accepts a
   season created *entirely in the past* (it exists to import off-season matches) and enqueues
   `SeasonCreatedPastDueEvent`, whose post-commit handler closes it on the spot. Announcing the start
   of a season that is closed by the end of the same round trip would deliver "Spring has started"
   and "Spring ended — you finished #3" together. So: when `EndsAt != null && EndsAt <= now`, stamp
   `StartAnnouncedAt` and write **nothing**. The close path still announces the result, which is the
   only part anybody wants.
2. **Scheduled** — nothing at creation. A new `AnnounceStartedSeasonsCommand(teamId)` is dispatched
   from `CurrentTeamProvider`'s existing **lazy team-page hook** — the one that already dispatches
   `CloseSeasonCommand` per due season. The command takes the season row lock, re-checks
   `StartAnnouncedAt == null && StartsAt <= now && ClosedAt == null`, writes the notifications
   and stamps the column. Idempotent, concurrency-safe, no scheduler.

   ⚠ **The lazy announce and the lazy close collide, and the close must win.** A season can run
   its whole course between two visits — scheduled, started, ended, all while nobody opened a team
   page. On the next visit it is simultaneously *unannounced* and *due to close*. Two rules keep
   that sane: the hook runs the **close loop first**, and the announce command's `ClosedAt == null`
   re-check then suppresses the start announcement — only "ended" is delivered, mirroring the
   creation-time guard above. And the unannounced *lookup* must itself filter `ClosedAt == null`:
   the suppressed season never gets its column stamped, so without that filter it would be
   returned — and pointlessly re-dispatched — on every page load forever.

   **Fold the lookup into the existing one rather than adding a second.** `CloseDueSeasonsAsync`
   runs `GetDueSeasonIdsQuery` on *every* `GetCurrentTeamAsync()` call — deliberately including the
   cached fast path, so the check can't fire once per multi-hour circuit — and `GetCurrentTeamAsync`
   is called by the layout and by pages. Bolting a second query beside it doubles a cost that is
   already paid more often than it looks. Replace both with one
   `GetTeamSeasonHooksQuery(teamId) : IQuery<TeamSeasonHooksDto>` returning
   `(List<int> DueClose, List<int> Unannounced)`, and have `CurrentTeamProvider` run one dispatch
   that drives both loops — close first (see above). The folded query keeps `GetDueSeasonIdsQuery`'s
   member gate, and its `Unannounced` filter is the full
   `StartAnnouncedAt == null && StartsAt <= now && ClosedAt == null`. The second list rides the new
   `(TeamId, StartAnnouncedAt, StartsAt)` index (§3.5); the first keeps using
   `(TeamId, ClosedAt, EndsAt)`.

- **Target**: `/team/{code}/seasons/{seasonId}`.
- Consequence to accept: the announcement lands when the first member opens a team page after
  the start, not at midnight. For an office foosball league that is fine and it is the honest
  trade for having no scheduler.

### 5.5 `SeasonEnded` + `SeasonAward`

- **Where**: after `SeasonCloseProcedure.CloseAsync`, inside the same transaction, before its
  `SaveChangesAsync` — **in both of its callers**. `CloseAsync` has two call sites, not one:
  `CloseSeasonCommandHandler` (the lazy close) *and* `EndSeasonNowCommandHandler` (a captain
  ending the season early). Hooking only the first would make an early-ended season finish in
  silence — no result, no awards, for exactly the close a human deliberately triggered. Both
  handlers make the identical write, fed by what `CloseAsync` returns (next paragraph), so the
  duplication is a few lines.

  **Have `CloseAsync` return what it froze** instead of digging it back out of the change tracker.
  It is `static Task CloseAsync(...)` today and adds `SeasonPlayerResult`/`SeasonAward` entities to
  the context; recovering them means walking `ChangeTracker.Entries<T>()` filtered to `Added`, which
  is both obscure and fragile against a future reordering. Returning a small
  `SeasonCloseResult(IReadOnlyList<(int PlayerId, int? FinalRank)> Ranks, IReadOnlyList<SeasonAward> Awards)`
  is a two-line change to a procedure that already has both lists in locals (`participants`,
  the podium lists), and it gives the notification write a typed input.
- **`SeasonEnded`** → every claimed member. `Value` = that member's `FinalRank`, or null if they
  didn't participate or were inactive at close (Web words both cases, §10).
- **`SeasonAward`** → one row per award the player won, `Category` + `Value` = rank, and
  `SubjectPlayerId` = the partner for `Pair` awards. A player sweeping everything gets at most
  four award rows plus the `SeasonEnded` row; collapsing them into one "you won 3 awards" row is
  a future option, not v1 (§15).

  Note that awards are generated **only** when the season has at least
  `Constants.Seasons.MinMatchesForAwards` (10) matches in total, so a short season closes with
  standings and no awards at all. That is the existing rule, not a notification rule — the write
  simply iterates whatever `GenerateAwards` produced, which may be nothing.
- **Note**: close is triggered lazily by whichever member loads a team page first (§2), so these
  rows are created by an arbitrary user's request. The close handler is already documented as a
  *system* action with no captain check — the notification write inherits that stance.
- **Target**: `/team/{code}/seasons/{seasonId}`.

---

## 6. Ladder leads — the four #1s

Settled with the user: notify on **#1 changes only**, across **four ladders** — solo, duo,
goalkeeper, attacker. These are exactly the four `Constants.Seasons.AwardCategories`, and exactly
the four tables the Rankings page already renders.

### 6.1 The four ladders — eight tables, six queries, three of them missing a tie-break

The evaluation **must not reimplement any of these** — the ranking rules are extracted into a
shared `LadderLeaders` helper (in `Features/Stats/Queries`, beside the queries that own the rules,
rather than in the Notifications slice: it is ranking logic that notifications *consume*) which the
existing queries then call, so the bell and the page can never disagree.

"Four ladders" is four *categories*, and each exists in two scopes — **eight tables across six
queries**, all of which the helper has to cover:

| Category | All-time query | Seasonal query |
|---|---|---|
| `Player` (solo) | `GetRankingsQuery` | `GetSeasonStandingsQuery` |
| `Goalkeeper` | `GetPositionRankingsQuery` | `GetSeasonPositionRankingsQuery` |
| `Attacker` | ″ (same query, second list) | ″ (same query, second list) |
| `Pair` (duo) | `GetPairRankingsQuery` | `GetSeasonPairRankingsQuery` |

And the orderings are **not** the same on both sides. This is what the seasonal three do today —
they mirror the award tie-break chains, deliberately, so a podium always matches its table:

| Category | Seasonal order | Eligibility |
|---|---|---|
| `Player` | seasonal ELO desc → wins desc → matches desc → `PlayerId` asc | active participants |
| `Goalkeeper` | conceded/GK-game asc → GK games desc → seasonal ELO desc → `PlayerId` asc | `GoalkeeperMatches >= MinGamesForPositionBadge` (5) |
| `Attacker` | scored/ATK-game desc → ATK games desc → seasonal ELO desc → `PlayerId` asc | same threshold (5) |
| `Pair` | win rate desc → matches desc → combined seasonal ELO desc → `min(PlayerId)` asc | `MatchesTogether >= MinGamesForPartnerStats` (3) |

The all-time three **stop short**, and that is the defect this feature cannot live with:

| Category | All-time order, as written today | What is missing |
|---|---|---|
| `Player` | `OrderByDescending(p => p.Elo)` — and nothing else | any tie-break at all; two players on equal ELO come back in whatever order the server feels like |
| `Goalkeeper` | avg conceded asc → games desc | ELO and `PlayerId` |
| `Attacker` | avg scored desc → games desc | ELO and `PlayerId` |
| `Pair` | win rate desc → matches desc | combined ELO and `min(PlayerId)` |

**Why an unstable tie is fatal here rather than cosmetic.** The snapshot comparison in §6.3 asks
"is the #1 the same row as last time". With no final tie-break, two players tied at the top can
swap places between two evaluations for no reason at all — and every swap looks exactly like a lead
change, so the bell fires `LadderLeadLost` at one of them and `LadderLeadTaken` at the other, on
every single match, forever. Equal ELO is not exotic in a small team: everyone starts at 1000, and
`EloCalculator.ApplyEloChange` clamps at 100.

So the shared helper defines the **full** chain for both scopes, ending in `PlayerId` asc
(`min(PlayerId)` for pairs), and **`GetRankingsQuery`, `GetPositionRankingsQuery` and
`GetPairRankingsQuery` adopt it**. Treat that as a prerequisite commit, not a side effect: it is
also a small bug fix in its own right — those three tables currently render ties in an arbitrary
order that can change between two loads of the same page.

**Pair eligibility is aligned in the same pass** (settled with the user, §16). The all-time pair
table is the only one of the eight that does not filter on `Player.IsActive`; the seasonal pair
table, the pair awards and all six other tables do. The helper filters inactive members out of both
pair ladders and `GetPairRankingsQuery` adopts that too — otherwise the bell can announce that you
lost the #1 duo spot to a pair that no longer exists. Visible side effect to accept: the all-time
duo table on Rankings stops listing pairs with a deactivated member.

Tie at #1 after all that: resolved deterministically, so there is always exactly one leader row. A
tie that the chain resolves *is* a lead change if the resulting leader differs from the snapshot —
accepted; it is rare, and the ranking page now shows the same thing.

### 6.2 Scope: one ladder set is *announced* per match — but both are always *refreshed*

`announce scope = match.SeasonId != null ? that season : all-time`.

A seasonal match announces changes in the season's four ladders; an off-season match (or any match
when no season is active) announces the all-time four. That is the user's "in the active season, or
generally when no season is active", and it keeps ladder notifications from doubling up during a
season.

**But a seasonal match changes the all-time ladders too, and pretending otherwise breaks the
snapshot.** `CreateMatchCommandHandler` always updates `Player.Elo` — the seasonal ladder is an
*additional* ELO pass, not an alternative one (verified: the all-time block runs unconditionally,
the `if (season != null)` block runs on top). So a seasonal match can move the all-time #1 while
the all-time snapshot is never looked at. It then says whatever it said weeks ago, and the next
off-season match compares against that stale row and announces a lead change that either happened
long ago or never happened at all — to people who have no way to make sense of it.

So (settled with the user, §16): **every evaluation refreshes both scopes; only the announce scope
notifies.** A seasonal match writes `Notify: true` for the season's four ladders and `Notify: false`
for the all-time four; an off-season match has only the one scope to do. The cost is nil — both
scopes are computed in memory from the same single match load (§6.3), so this is a flag, not a
second pass. The invariant it buys is worth stating plainly: **a `LadderLeader` row is never allowed
to be out of date, whether or not anyone was told.** Everything else in §6 depends on that.

### 6.3 `EvaluateMatchAftermathCommand` (and its silent sibling)

```csharp
public sealed record EvaluateMatchAftermathCommand(int TeamId, int MatchId, int? SeasonId, bool Notify) : ICommand;
public sealed record RefreshLadderLeadersCommand(int TeamId) : ICommand;   // always silent (see below)
```

`SeasonId` rides the command (copied from the event) **because the handler must not load the match
row to learn it** — on the delete path that row no longer exists. It is also what keeps the delete
path's refresh honest for a *pending-close* season: `RefreshLadderLeadersCommand`'s "active season"
probe would skip a season whose `EndsAt` has passed but which is not yet closed, while the deleted
match's own `SeasonId` names it exactly.

`Notify` means "may announce at all", not "which scope" — the handler derives the announce scope
from the command's `SeasonId` per §6.2 and keeps the other scope silent regardless. So
`Notify: false` (the delete path) means both scopes are refreshed and nobody is told.
`RefreshLadderLeadersCommand` refreshes all-time plus the team's active season, if it has one, and
announces nothing anywhere.

**Where it runs.** *Not* inside `CreateMatchCommand` — the aftermath needs the team's whole match
history (the pair and position ladders aggregate every match), and running that inside the match
transaction would hold the season row lock (which the handler takes on the seasonal path) for the
duration. It runs **post-commit, synchronously, as a nested dispatch** (settled with the user,
§16), which is the pattern the codebase already has:

1. `CreateMatchCommandHandler` enqueues `MatchRecordedEvent(teamId, matchId, seasonId, playerIds)`
   on `IEventCollector` — note the handler does not reference the collector today; injecting it is
   part of the edit.
2. An **Application-side** `MatchRecordedBridge` handles it post-commit and awaits the aftermath
   through the scope's own `ISender`. It lives beside the command it dispatches, exactly where the
   precedent lives: `SeasonCreatedPastDueEventHandler` sits in `CreateSeasonCommand.cs` in
   Application, **not** in Web — an earlier draft misplaced this bridge in Web, but nothing about
   the dispatch touches a Web concern (only the created-notification → notifier bridge needs Web):
   ```csharp
   internal sealed class MatchRecordedBridge(ISender sender, ILogger<MatchRecordedBridge> logger)
       : INotificationHandler<MatchRecordedEvent>
   {
       public async Task Handle(MatchRecordedEvent e, CancellationToken ct)
       {
           // Published post-commit, so the create transaction is finished — this dispatch opens
           // its own through the normal pipeline (same shape as SeasonCreatedPastDueEventHandler).
           var result = await sender.Send(new EvaluateMatchAftermathCommand(e.TeamId, e.MatchId, e.SeasonId, Notify: true), ct);
           if (result.IsFailure)
               logger.LogError("Match aftermath failed for match {MatchId}: {Error}", e.MatchId, result.Error.Code);
       }
   }
   ```
   **Why this works, mechanically:** `TransactionBehavior` commits and *then* drains the collector,
   and `HasActiveTransaction` reads `Database.CurrentTransaction`, which EF Core clears on commit.
   So the nested dispatch sees no ambient transaction and opens its own — which is exactly what
   `IDbLocks` needs in step 4. The outer flush already materialised its own list before publishing,
   so the inner `Drain()` returns only the inner command's events; the two do not interleave.
   `SeasonCreatedPastDueEventHandler` relies on all of this today.

   **Why synchronous rather than `Task.Run`:** it costs the recorder a round trip (see §6.4) and
   buys three things a background task cannot. Nothing is lost on shutdown, so the notifications
   are never silently dropped. The recorder's own circuit sees the resulting rows as part of the
   round trip. And there is no principal problem — a background task would have to be handed a
   `ClaimsPrincipal` for `ScopedDispatcher`, and a bridge handler has none: the Application scope
   carries `IUserContext` (a user id and an admin flag), not a principal. Capturing scoped services
   in a detached task would also be a use-after-dispose, since the dispatch scope dies when the
   outer `Send` returns. §6.4 keeps `Task.Run` documented as the escape hatch if this ever measures
   badly — the design does not change shape if it moves.
3. **The handler takes `IDbLocks.AcquireTeamTimelineLockAsync(teamId)`** — two matches recorded
   seconds apart would otherwise evaluate concurrently and write contradictory snapshots. The
   lock already exists for exactly this class of per-team serialization.
4. **One load, both scopes.** It loads:
   - the team's matches with their `MatchPlayer` rows (`Matches.Include(m => m.MatchPlayers)`),
     ordered `PlayedAt` then `Id` — the all-time ladders, and every milestone in §6.5, come out of
     this;
   - the team's players (needed for `IsActive` and for names at write time);
   - **and, when the match was seasonal, that season's `SeasonPlayers`** — the seasonal solo ladder
     ranks on `SeasonPlayer.Elo`, which no amount of match data substitutes for. This is the piece
     the earlier draft omitted; `SeasonAggregateLoader.LoadLiveAsync` is the existing shape to
     follow, and the seasonal aggregates come from filtering the already-loaded matches by
     `SeasonId` rather than a second query.

   Then, for each of the eight (scope, category) pairs — announcing only in the announce scope
   (§6.2) — it compares the computed #1 against the `LadderLeader` row:
   - **No snapshot row** → write it, **notify nothing**. This is the backfill guard (§3.6): the
     first evaluation for a team must not announce four leads that have been true for months. See
     §3.4 for the two accepted consequences.
   - **Same leader** → nothing.
   - **Changed** → update the row; if `Notify`, write `LadderLeadTaken` to the new leader (both
     members for `Pair`) and `LadderLeadLost` to the previous one (both members), each carrying
     the other side in `SubjectPlayerId`. A pair change where one member is unchanged still
     notifies both members of both pairs — the duo is the unit.
   - **Ladder became empty** (last eligible player deactivated) → delete the row, notify nothing.

**After a match deletion.** `DeleteMatchCommandHandler` reverses ELO, so leads can revert. It
enqueues the same event with `Notify: false` — the snapshot is refreshed **silently**. Without
this, the snapshot goes stale and a later match produces a "you took #1" for a lead nobody ever
lost. (It also runs the §3.1 notification cleanup for that match.) Worth knowing the actual
deletion rules in `MatchRules.DeletionBlockerAsync`: a 24-hour window measured from the match's
`CreatedAt`, never a match of a **closed** season, and no participant may have a match with a
higher id — so a deleted match is always the newest one for its players, which is why a plain
re-evaluation is a sufficient repair.

**The other way a ladder moves without a match: (de)activation.** All four ladders filter on
`Player.IsActive` in both scopes once §6.1 aligns the one that didn't, so `DeactivatePlayerCommand` and
`ReactivatePlayerCommand` can change a #1 with no match involved — and today nothing would notice,
leaving the snapshot stale until the next match blamed an innocent match for it. Both handlers
enqueue a `RefreshLadderLeadersCommand(teamId)`: the same evaluation, **always silent, no
milestones, no `MatchId`**. Deliberately silent rather than announced — "you are now #1 because
somebody left" is not a thing to celebrate, and the honest notification for it does not exist. Kept
as a separate command from `EvaluateMatchAftermathCommand` so neither has a nullable `MatchId` and a
`bool` doing two jobs.

**And a third way: season-boundary edits.** `UpdateSeasonEndsAtCommand` (shrinking `EndsAt`)
unassigns the season's tail matches and **replays the whole seasonal ladder** through
`SeasonLadderReplay` — `SeasonPlayer.Elo` changes with no match recorded, so the season's snapshot
rows go stale exactly like the deactivation case. The handler enqueues the same silent
`RefreshLadderLeadersCommand(teamId)`. The other two `SeasonLadderReplay` call sites need nothing:
`CreateSeasonCommand`'s match import replays a season that was *just created* and therefore has no
snapshot rows yet (the silent-first-write guard covers it), and `DeleteSeasonCommand` already
clears its rows outright (§3.4).

### 6.4 Cost, and when to revisit

One aftermath run = one team-timeline lock + one load of the team's matches with their
`MatchPlayer` rows (+ the season's `SeasonPlayers` when seasonal) + in-memory math for eight
ladders and four milestone rules. For a team with ~2 000 matches that is ~8 000 rows — the same
order as the Stats page, which already does this synchronously on demand, and the Rankings page
does most of it too.

Since §6.3 runs it synchronously, this now sits **on the "Record match" round trip**, once per
recorded match. That is the trade taken knowingly: correct-and-simple over fast, at a scale where
the same query shape already backs a page nobody complains about. Two escape hatches, in order of
preference, neither of which changes the design's shape:

1. move the dispatch to a background task (§6.3 spells out what that costs: a lost run on shutdown,
   and a synthesized principal or a `ScopedDispatcher` overload);
2. store per-player/per-pair running aggregates and evaluate incrementally.

**Do not** reach for either before measuring — "Record match" is a deliberate, low-frequency
action, and a team large enough for this to hurt is a team worth having numbers from first.

### 6.5 Personal milestones (same run, same load)

Computed from the same in-memory data, always over the **all-time** history (a "personal best" is
not scoped to a season), for the four players in the match only:

| Type | Rule |
|---|---|
| `PeakElo` | the player's `EloAfter` in this match exceeds their previous max `EloAfter` across all matches. Only when the previous max exists — a player's very first match is not a "new peak". |
| `WinStreak` | current consecutive wins (`StreakComputer`'s rule: win by score, ordered `PlayedAt` then `Id`) reaches a threshold: **3, 5, 10, then every 5**. |
| `MatchMilestone` | total matches played hits **10, 25, 50, 100, then every 100**. |
| `NemesisBeaten` | the player won, and one of the two opponents is **the** player's nemesis: the single opponent with their highest loss ratio, over ≥ `Constants.Stats.MinHeadToHeadGames` (4) head-to-head games, ratio > 0.5, **computed excluding this match** — otherwise the win itself can flip the ratio and the notification contradicts its own premise. |

**Two notes on reusing the existing rules, mirroring §6.1's "don't reimplement":**

- `StreakComputer.Compute` is directly callable from the aftermath (`internal static`, and the
  aftermath lives in the same assembly) — build a `StatContext` over the already-loaded matches
  with `Ladder = EloLadder.AllTime, IsFullScope = true` (the context also carries
  `SeasonPlayersById` and ELO accessors; the seasonal parts stay unset in all-time scope).
  `PeakEloStat` is **not** the thing to call, though: its `Compute` is `protected`, and the public
  `StatBase.Calculate` returns the *team-wide leaderboard holders*, not a per-player peak. The
  rule itself is one expression — the player's max `EloAfter` over their earlier matches — so the
  aftermath computes it inline. Reuse the rule, not the class.
- **`NemesisStat` does need one.** Its pairing math is a `protected override Compute` on a
  `StatBase`, so it is not callable, and it additionally narrows to the team-wide worst ratio because
  it is a leaderboard entry — which is not the question here. Extract the per-player
  "who beats me most" computation into `Rivalries/NemesisRule` and have both `NemesisStat` and the
  aftermath call it, exactly as §6.1 does for the ladders. Give it a **deterministic tie-break**
  while extracting: it currently takes `.First()` after `OrderByDescending(ratio)`, so a player with
  two equally dominant opponents gets an arbitrary one — harmless in a leaderboard, but here it
  decides whether a notification fires. Break on more head-to-head games, then lower `PlayerId`.

**Loss streaks are deliberately not in v1** — the app's humour supports it (`SlumpKingStat`
exists), but a notification that pings you about losing is the one most likely to make someone
turn the feature off. **Settled: excluded** (§16). Note that the whole group is one
preference toggle away from silence anyway (§8), which lowers the stakes on this call.

Target for all four: `/team/{code}/players/{playerId}` (the player detail page, which is where
these numbers live). `LadderLeadTaken`/`Lost` target `/team/{code}/rankings` — for a seasonal
scope, `/team/{code}/seasons/{seasonId}`.

---

## 7. Read / unread

### 7.1 Two tiers, per row — not a watermark

Chat uses a `LastReadMessageId` watermark because a conversation is read in order. A feed is not:
you click the interesting row and leave the rest. So state is **per row**, and it has two tiers,
because "the badge should stop nagging" and "I have actually looked at this one" are different
questions:

| Field | Meaning | Drives |
|---|---|---|
| `SeenAt` | the feed has been shown to you since this arrived | the **badge count** |
| `ReadAt` | you opened this particular notification | the row's **unread dot + weight** |

Why two rather than one: `MatchRecorded` fires on every match somebody else enters, so in an
active team a badge that only clears row-by-row would realistically never reach zero — and a
badge that never reaches zero is one people stop reading. Splitting the tiers keeps the badge
meaningful *and* preserves "you can view them later multiple times and see what you have seen",
which is `ReadAt`'s whole job.

### 7.2 What sets each

- **Opening the panel** → `MarkNotificationsSeenCommand()`: one `ExecuteUpdate` stamping `SeenAt`
  on **every** unseen row of the current user, not just the page that rendered. The badge means
  "arrived since you last looked", and opening the panel *is* looking; a badge that stayed lit
  because unseen rows sat below the fold would reintroduce exactly the problem this tier solves.

  ⚠ **"Opening the panel" is not an event Blazor gets to see.** §9.1 hands open/closed state to
  `ui.js`, which keeps it in the DOM as a `.show` class precisely so a re-render cannot disturb it —
  and it never tells .NET. A mirrored `_open` flag in the component would drift the moment the user
  dismisses with Escape or an outside click (both of which `ui.js` handles without telling anyone),
  and a drifted flag inverts: the next click marks nothing seen while the menu opens.

  The way out is to stop caring about the direction. The trigger button carries **both**
  `data-toggle="dropdown"` (ui.js opens/closes it) and an `@onclick` that runs one idempotent
  "the user touched the bell" routine: load the first page if it has not been loaded this circuit,
  then dispatch `MarkNotificationsSeenCommand`. Running it on the *closing* click too is harmless —
  the rows were just seen, and a repeat `ExecuteUpdate` matches zero rows. Both listeners fire:
  `ui.js` calls `preventDefault()` (which suppresses only the browser's default action, not other
  handlers) and never `stopPropagation`, so Blazor's delegated handler still runs.

  **Verify this pairing early** — no element in the app carries `data-toggle="dropdown"` and
  `@onclick` today, so it is the one mechanism here without a precedent to copy. If it disappoints,
  the fallback is a `DotNetObjectReference` callback from `ui.js` on open, which is more plumbing
  for the same outcome.
- **Clicking a row** → `MarkNotificationReadCommand(id)`, then navigate / open the dock.
- **"Mark all as read"** in the panel header → `MarkAllNotificationsReadCommand`, an
  `ExecuteUpdate` over the user's unread rows. Still a first-class button: it is how you clear a
  backlog of bold rows you have decided not to open.
- `ReadAt` implies `SeenAt` — any write that stamps `ReadAt` stamps `SeenAt` too if it is null,
  so the two can never contradict (a row you read but never "saw" is nonsense, and would leave
  the badge counting it).

Every write raises `NotificationReadStateChangedEvent(userId)` so the same user's **other tabs**
update, exactly as chat's `ReadStateChanged` does.

Badge = `count(Notification where UserId = me and SeenAt is null)` — one filtered-index count,
recomputed (never incremented) on every event, the same discipline as chat's unread counts.

### 7.3 Cross-feature rule: reading the chat clears its notifications

A mention or reaction you already saw in the chat panel should not keep nagging in the bell.

**Hook it on `ChatReadStateAdvancer.AdvanceAsync`, not on `MarkChatReadCommandHandler`.** The
watermark advance lives in that shared helper, and **two** commands call it: `MarkChatReadCommand`
*and* `SendChatMessageCommand` — sending inherently marks the conversation read, because you were
looking at it. Hooking only the mark-read command would mean that answering a mention in the chat
leaves the bell row bold while the chat panel considers the whole thread read, which is the precise
contradiction this section exists to prevent. The helper already receives everything needed
(`db`, `events`, `userId`, `teamId`, `messageId`) and already owns the "did it move" decision.

So when it advances a watermark to `M` for team `T`, it also runs:

```csharp
db.Notifications
  .Where(n => n.UserId == me && n.ReadAt == null && n.TeamId == T)
  .Where(n => n.Type == NotificationType.ChatMention || n.Type == NotificationType.ChatReaction)
  .Where(n => n.ChatMessageId != null && n.ChatMessageId <= M)
  .ExecuteUpdateAsync(s => s
      .SetProperty(n => n.ReadAt, now)
      .SetProperty(n => n.SeenAt, n => n.SeenAt ?? now));   // §7.2: ReadAt implies SeenAt
```

(one extra `ExecuteUpdate` on a path that already writes) and raises the read-state event if any
row changed. This is the one place the two features are coupled, and it is worth it: without it
the bell contradicts the chat panel.

Two details that fall out of the placement. It sits inside the member-gated path, so
`MarkChatReadCommand`'s "silently succeed for a non-member" branch never reaches it. And the
advancer has an early `return` for "already at or above `M`" — put the update **before** that
return only if you want a re-read of an unchanged watermark to still clear rows; putting it after
the successful-advance paths is enough, because the rows can only have been created below a
watermark that has not yet moved past them.

### 7.4 Retention

v1 keeps everything, like chat history. The feed loads
`Constants.Notifications.PageSize` (20) rows at a time with a `LoadMoreButton`. If volume ever
matters, pruning read rows older than N days is a single `ExecuteDelete` that can hang off the
same lazy team-page hook as §5.4 — noted, not built.

---

## 8. Preferences (per team)

Settled with the user: notifications are configurable, and the configuration is **per team** —
"everything from Alfa, only the ladder changes from Beta". A single global switch cannot express
that, and since every trigger is team-scoped (§1) the team is the natural axis.

### 8.1 Five categories, not twelve types

Twelve toggles per team is a settings page nobody reads. The types group into five categories
that match how people actually think about them — and the user's own example ("only placement")
is exactly one of them:

| `NotificationCategory` | Types | Roughly |
|---|---|---|
| `Matches` | `MatchRecorded` | "someone entered a match with me" |
| `Chat` | `ChatMention`, `ChatReaction` | "someone mentioned me / reacted to me" |
| `Seasons` | `SeasonStarted`, `SeasonEnded`, `SeasonAward` | "season lifecycle and my result" |
| `Rankings` | `LadderLeadTaken`, `LadderLeadLost` | "the #1 spots changed" |
| `Milestones` | `PeakElo`, `WinStreak`, `MatchMilestone`, `NemesisBeaten` | "personal highlights" |

The map lives in **SharedKernel** (`NotificationCategories.Of(NotificationType)`), a pure switch
**guarded against CS8509** so a new type cannot be added without classifying it. It sits there
rather than in Application because both Application (the write filter) and Web (grouping the
settings UI) need it, and it is a classification, not presentation — the category *labels* are
Web's job (§10).

### 8.2 Resolution and defaults

```
Channels(user, team, category) = row(user, team, category)?.Channels ?? Default
Default (v1) = NotificationChannel.InApp        // everything on
```

- **Everything is on until you turn it off.** A new user, a newly joined team, and a category
  nobody has touched all resolve to the default without a row existing (§3.3 is sparse).
- **Joining a team does not need a write** — no seeding, nothing to keep in sync when someone
  joins or leaves.
- `Channels` is a flags enum so phase 2's push toggle is a second bit on the *same* row (§13),
  not a second table and not a migration.
- The reserved `TeamId == null` tier ("my default for every team") is designed for but **not
  built** in v1 — deferred by decision (§16).

### 8.3 Enforcement: at write time, not at read time

`INotificationWriter.AddAsync` resolves the draft's category, loads the muted recipients for that
`(team, category)` and drops them before inserting:

```csharp
var category = NotificationCategories.Of(draft.Type);
var muted = await db.NotificationPreferences
    .Where(p => p.TeamId == draft.TeamId && p.Category == category && recipientIds.Contains(p.UserId))
    .Where(p => (p.Channels & NotificationChannel.InApp) == 0)
    .Select(p => p.UserId)
    .ToListAsync(ct);
```

Because rows are sparse and only *overrides* exist, this query normally returns nothing and
touches a handful of rows at most. Within one dispatch (notably the aftermath run, §6.3, which
writes several drafts) the writer caches the lookup per `(teamId, category)` so a batch is one
query per category, not one per draft.

**Filtering at write time, not at display time, is the deliberate choice:**
- The unread count can never disagree with what the feed shows.
- No dead rows accumulate for categories a user muted years ago.
- **Consequence to accept**: turning a category back on does **not** reveal what you missed while
  it was off. That is the honest reading of "mute" and it is what every messaging app does.

Muting is also **not** retroactive in the other direction: rows already written stay in the feed
until read. Turning `Chat` off does not silently empty your bell.

### 8.4 Commands & queries

```csharp
public sealed record GetNotificationPreferencesQuery()                                  // current user
    : IQuery<List<TeamNotificationPreferencesDto>>;                                     // one per claimed team
public sealed record SetNotificationPreferenceCommand(int TeamId, NotificationCategory Category, bool InAppEnabled)
    : ICommand;
```

- Both are **owner-scoped**: neither takes a user id; both read `IUserContext.UserId` (§11).
- The query returns one entry per team where the user has a **claimed player**, with all five
  categories materialised from stored rows + defaults — the UI never has to know about sparseness.
  Note that `GetMembershipOverviewQuery` is *not* itself that rule: it returns every membership with
  `MyPlayer` possibly null, and callers filter. `ChatDock` does exactly this
  (`.Where(o => o.MyPlayer != null)`) and is the shape to copy.
- The command validates that the user has a claimed player in that team, then upserts. Setting a
  category back to the default value could delete the row instead of storing it; **don't** — an
  explicit row is a record of an explicit choice, and it is what the future global tier will need
  to override correctly.

### 8.5 Where the settings live

Two entry points, one destination (settled with the user):

**The cog in the bell panel.** A `bi bi-gear` button in the panel header, beside "Mark all as
read". It closes the panel and navigates to the Account page's notifications section.

**The Account page section.** A new `.panel` titled **Notifications**, placed after the existing
**Teams** panel (it is a per-team setting, so it reads in the right order), built as
`Components/Notifications/NotificationSettings.razor`:

```
┌─ 🔔 Notifications ───────────────────────────────────────────┐
│ Choose what each team can notify you about.                  │
├──────────────────────────────────────────────────────────────┤
│ 🛡 Alfa Team                                    All · None   │
│    Matches      ●━  Chat        ●━  Seasons     ●━           │
│    Rankings     ●━  Milestones  ●━                           │
├──────────────────────────────────────────────────────────────┤
│ 🛡 Beta Team                                    All · None   │
│    Matches      ━○  Chat        ━○  Seasons     ━○           │
│    Rankings     ●━  Milestones  ━○                           │
└──────────────────────────────────────────────────────────────┘
```

- One `list-group-item` per team, matching the existing Teams panel's markup, with five
  `.form-switch` toggles — the design system already ships `.form-switch` (`Styles/components/form.css`),
  so no new CSS primitives are needed.
- **"All / None"** per team is a UI shortcut that writes all five categories; there is no separate
  "team muted" concept in storage.
- Teams where the user has **no claimed player** are not listed — they cannot produce
  notifications at all (§1) — with a one-line note pointing at the claim link, mirroring how the
  Teams panel already handles that state.
- **Saving is per toggle, immediately, with no toast.** A switch flipping is its own feedback,
  which is exactly `ToastService`'s documented "neither" case. A failed save flips the switch back
  and renders an inline `AlertMessage` inside the panel — where the user can retry it.
- Account.razor already loads `GetMembershipOverviewQuery`, so the panel reuses that list and adds
  one query for the preferences.

**Scroll-to-section gotcha.** Account renders its panels only after `OnAfterRenderAsync` finishes
loading (verified — `GetMembershipOverviewQuery` is dispatched there), so a plain
`/account#notifications` fragment will not scroll: the element does not exist when the browser
handles the fragment. Use `/account?section=notifications` plus a `scrollIntoView` after the data
loads.

**The helper goes in `wwwroot/js/app.js`, not `ui.js`.** `app.js` is where every `window.*` interop
helper already lives (`getBrowserTimeZone`, `fotbalekTheme`, `fotbalekToast`, the chart renderers) and
is the file `IJSRuntime.InvokeVoidAsync("…")` reaches by name. `ui.js` is a closed IIFE with **no
export surface at all** — dropdown and collapse behaviour delegated from the document, nothing
callable from .NET — so "add an exported helper there" is not a thing that file can do without
changing what it is. One more `window.fotbalekScrollToId = function (id) { … }` beside its siblings
is the whole change.

Remember that both JS and CSS are fingerprinted by `MapStaticAssets` at build time: a dev-server
restart (not just a rebuild) is what makes an edit to either show up.

### 8.6 Future: quick mute from the feed

The highest-value follow-up is muting *from where the annoyance happens*: a `⋯` on a notification
row offering "Stop telling me about {category} in {team}", which writes the same preference row.
Listed in §15, not v1 — but the storage model is already shaped for it.

---

## 9. UI / UX — the bell

### 9.1 Placement: the team navbar, next to the account menu

Settled with the user after a review of what comparable apps do. A bell in the **top-right
chrome beside the avatar**, opening an anchored dropdown, is the near-universal convention
(GitHub, Linear, Jira, Notion, Vercel, Figma, Azure DevOps; Discord's Inbox and Slack's Activity
are the same idea in a left rail). A floating circle in the bottom-right corner means something
else entirely — it is the support-widget idiom (Intercom, Crisp, Zendesk) and Facebook's
Messenger bubble — so a floating *bell* beside the chat launcher would have read as "a second
chat thing". Chat and notifications are also kept as separate surfaces in essentially every app
that has both; merging them into one dock is not a pattern worth inventing here.

`TeamLayout`'s navbar already has exactly the right slot: its right-hand actions group holds the
presence chip, the live-game pill, "New match" and the **account dropdown** (avatar + name). The
bell goes immediately left of the account dropdown.

```
┌─ TeamLayout navbar ───────────────────────────────────────────┐
│ 🏆 Alfa Team    Dashboard   Insights ▾   Seasons   …           │
│                        👥 3   🎮   + New match    🔔3   🦁 ▾   │
└─────────────────────────────────────────────┬─────────────────┘
                                              │ ┌──────────────────────────────┐
                                              └▸│ Notifications   ✓ all    ⚙   │
                                                ├──────────────────────────────┤
                                                │ — Today —                    │
                                                │ ●🦁 Alice recorded a match   │
                                                │      with you       10:42    │
                                                │ ●🏆 You're #1 goalkeeper     │
                                                │      Alfa Team      10:42    │
                                                │   🐺 Bob reacted 👍 to your  │
                                                │      message        09:15    │
                                                │ — Yesterday —                │
                                                │   🎖 You finished #2 in      │
                                                │      Spring Season           │
                                                ├──────────────────────────────┤
                                                │          Load more           │
                                                └──────────────────────────────┘
```

**This is a plain `.dropdown` + `.dropdown-menu.dropdown-menu-end`**, driven by the existing
`data-toggle="dropdown"` machinery in `wwwroot/js/ui.js` — the same markup the account, presence
and team-switcher menus already use. (A shared `Components/Shared/Dropdown.razor` wrapper also
exists; check whether its parameter surface fits before hand-rolling the markup.) That buys three
things and costs no new plumbing:

- **A live-updating panel cannot close underneath the user.** `ui.js` deliberately keeps open
  state in the DOM as a `.show` class rather than in component state, precisely so a Blazor
  re-render can't disturb it — its header names the live presence list and live-game viewer list
  as the reason. A feed that re-renders whenever a notification arrives needs exactly that
  property; a Blazor-owned `IsOpen` flag would have to fight it.
- **Outside-click and Escape dismissal are already implemented**, including returning focus to
  the trigger.
- **Click-through behaviour is already correct** — with one detail that must not be missed.
  `ui.js` closes the menu when a click inside it hits `.dropdown-item`, `a[href]` or a `button`,
  *unless* the button carries **`data-keep-open`**. So:

  | Control | Markup | Result |
  |---|---|---|
  | A notification row targeting a page | `a[href]` (or `.dropdown-item`) | navigates, menu closes ✓ |
  | A row targeting chat (mention/reaction) | plain `<button>` | opens the dock, menu closes ✓ |
  | **"Mark all as read"** | `<button data-keep-open>` | acts, menu **stays open** ✓ |
  | **"Load more"** | `<button data-keep-open>` | appends a page, menu **stays open** ✓ |
  | The **⚙ cog** | plain `<button>` | navigates to Account, menu closes ✓ |

  ⚠ **`data-keep-open` exempts a `button` and nothing else.** The selector is
  `.dropdown-item, a[href], button:not([data-keep-open])`, so a keep-open button that *also* carries
  `.dropdown-item` — the obvious thing to reach for when styling a menu header — matches on the first
  alternative and closes the menu anyway. Style those two buttons with anything but that class.

  `LoadMoreButton` (the shared component) does not pass arbitrary attributes today — either give
  it an `AdditionalAttributes` splat or use a plain button here. It also takes `Remaining` as
  `[EditorRequired]` to render its "(N more)" hint, and a keyset cursor has no total to put there:
  fetch `PageSize + 1` rows, render `PageSize`, use the extra as the has-more flag, and pass
  `Remaining="0"` (the hint hides at zero). A plain button is the simpler answer here given the
  attribute problem anyway.

Other panel details:

- **Trigger**: `bi bi-bell-fill` + the unread badge, capped at `99+` like chat's, plus the
  `@onclick` of §7.2. A subtle one-shot shake on arrival is the only arrival cue (see below); note
  that re-triggering a CSS animation needs the class removed and re-added, so drive it from an
  incrementing counter in the class list (or a short timer that clears it) rather than a bare bool
  that is already `true` when the second notification lands.
- **Header**: "Mark all as read" (hidden at zero unread) and the **⚙ cog** (§8.5). No close
  button — dropdowns dismiss on outside click / Escape, and the app's other menus have none.
- **Rows**: grouped by **local** day via `TimeZoneService`; unread rows carry a dot and heavier
  weight; the team name shown as a chip only when the user belongs to more than one team;
  relative/short time in the same format as the chat rail (`HH:mm` today, `ddd` this week,
  `MMM d` older).
- **Row icon**: the **actor's avatar when the row has an actor, a per-type icon when it does not.**
  Not "an avatar when there is one" — `Player.AvatarId` is a non-nullable `int` defaulting to 1, so
  every player always has one. The real split is actor rows (mention, reaction, match recorded)
  versus system rows (season lifecycle, **ladder leads**, milestones), which is exactly
  `ActorPlayerId != null` — §3.2 gives the lead rows no actor (§4.2: they are actor-less system
  writes, precisely so the recorder can receive their own), so they take the per-type icon. If a
  face ever seems better on "…took the #1 spot from you", `SubjectPlayerId` is already on the row.
- **Sizing**: the menu is wider and taller than the existing ones — a fixed width with
  `max-height` + internal scroll, i.e. what `.presence-menu` already does
  (`min-width: 230px; max-height: 340px; overflow-y: auto`). That belongs in
  **`Styles/components/team-navbar.css`**, beside those rules — *not* `nav.css`, which holds the
  generic bar shell and has no `.dropdown-menu` rules at all (the base lives in `overlay.css`), and
  not a scoped `.razor.css`: the markup is a navbar dropdown and should be styled with its siblings.
- **Empty state**: the shared `EmptyState` component. When the feed is empty *because* everything
  is muted, say so and link to the settings — otherwise it reads as a bug.

**No toast on arrival, and no banner surface.** `ToastService`'s own documented rule is that a
toast means *an action you took finished*; an incoming notification is not that. Chat already
owns the "something happened elsewhere" banner. The badge plus the shake is the whole arrival
story in v1.

### 9.2 Mobile

The navbar's actions group stays visible when the sections collapse (it is
`order-2 lg:order-3 ms-auto lg:ms-0`, a sibling of the collapsible `#teamSections` region), so the
bell is present on phones without any extra work. The
menu itself must not stay a narrow dropdown at that width: below the `sm` breakpoint it becomes a
near-full-width sheet pinned under the navbar, with full-width tap-target rows. "Mark all" and
the cog stay in the header.

### 9.3 Live behaviour

`NotificationBell` mirrors `ChatDock`'s wiring: subscribe to `NotificationNotifier` in
`OnAfterRenderAsync(firstRender)`, marshal every handler through `InvokeAsync(StateHasChanged)`
(events are raised on other circuits' threads), swallow `ObjectDisposedException`, unsubscribe in
`DisposeAsync`.

Two simplifications fall out of the navbar placement:

- **Open state is not Blazor's problem** — `ui.js` owns it (§9.1). `NotificationUiState` shrinks
  to the unread-count cache plus its `Changed` event; there is no `IsOpen` to persist. The price is
  that Blazor also cannot *observe* opening, which §7.2 handles with an idempotent trigger handler
  instead of a mirrored flag.
- **No layout-swap problem.** `TeamLayout` persists across in-circuit navigation between team
  pages, so the component instance survives; switching teams uses `forceLoad` and starts a fresh
  circuit anyway.

The feed is **loaded lazily on first interaction with the bell**, not on every team page load — the
badge count is one filtered-index query, the page of rows is only fetched once the user actually
touches the trigger (§7.2). Arriving notifications while the panel is closed bump the badge from the
notifier event and do not fetch anything; once the feed *has* been loaded this circuit, an arrival
prepends the DTO carried on the event.

The feed query is **account-scoped and cross-team** (§1) — it takes no team parameter, and
neither does the badge count:

```csharp
public sealed record GetNotificationsQuery(int? BeforeId, int Take) : IQuery<List<NotificationDto>>;
public sealed record GetUnseenNotificationCountQuery() : IQuery<int>;          // the badge (§7.1)
```

`BeforeId` is the cursor over the monotonic `Id` (§3.1), so "load more" is a keyset page on the
`(UserId, Id DESC)` index, not an offset. Being rendered inside team Alfa's navbar does not
narrow either one: a mention from team Beta appears in the same list, wearing Beta's chip.

Navigating to `/account?section=notifications` from the cog leaves `TeamLayout` (Account is a
`MainLayout` page), which disposes the bell — expected, and the reason the settings page has its
own way back.

### 9.4 The one nav-less page that matters: Home

The bell lives in `TeamLayout`, so it is absent from `/`, `/account`, `/create`, `/join` and
`/claim`. Four of those are transient onboarding/account pages nobody waits on. `/` is different:
it is the overview a logged-in user lands on, and its "Your Teams" list **already renders
per-team chat unread badges** (chat.md §5.2).

So Home gets a small bell badge next to the existing chat badge, from a new
`GetUnseenNotificationCountsByTeamQuery` → `Dictionary<teamId, int>` (unseen, matching the bell's
badge — so opening the bell once clears both surfaces, which is the consistent reading of "new
since you last looked"). Deliberately a **one-shot query on load, with no live subscription**: nothing on `MainLayout` maintains a notification cache
(the chat badges there are live only because `ChatDock` is present), and Home is a page you pass
through. If two badges per row prove noisy, merging them into a single "activity" count is the
obvious simplification — but they mean different things, so start separate.

---

## 10. Presentation (Web-owned)

`Web/Services/NotificationPresentation.cs`, following `StatPresentation.cs` — where, to be precise,
the static presentation class is named `StatDisplay` and `StatPresentation` is the record it
returns: a static class turning a `NotificationDto` into what the row renders, plus the labels for
the five preference categories.

```csharp
public static (string Icon, string Title, string? Detail) Describe(NotificationDto n);
public static NotificationTarget Target(NotificationDto n);            // Url(string) | OpenChat(int teamId)
public static (string Icon, string Label, string Hint) Describe(NotificationCategory c);
```

All three are exhaustive `switch` expressions **guarded against CS8509**, so adding an enum value
breaks the build until the wording and the target exist — the pattern the stats presentation
already uses (`WarningsAsErrors` promotes CS8509 in `Directory.Build.props`; write the switch with
no `_ =>` arm to opt in). Copy `StatDisplay`'s companion detail too: it wraps the block in
`#pragma warning disable CS8524`, because an exhaustive-over-named-members switch still warns about
out-of-range casts, and an out-of-range enum value here is a bug worth throwing on.

All English copy, all icons and all routes live here and nowhere else; Application and the entities
know only the enums, the ids and the numbers (repo convention, §3.2/§8.1). The file lands under
`Web/Services`, which Tailwind's `@source './../Services/**/*.cs'` glob already scans — so the CSS
classes named in it (badge colours, icon classes) do get generated, the same way
`StatPresentation.cs`'s do. Keeping presentation in Web is what lets that glob stay inside one
project.

Wording sketch (not binding — this is the layer to bikeshed in):

| Type | Title |
|---|---|
| `MatchRecorded` | "{Actor} recorded a match with you" |
| `ChatMention` | "{Actor} mentioned you" |
| `ChatReaction` | "{Actor} reacted {emoji} to your message" |
| `SeasonStarted` | "{Season} has started" |
| `SeasonEnded` | rank ? "{Season} ended — you finished #{rank}" : "{Season} ended" |
| `SeasonAward` | "You won {rank-ordinal} place: {category}" |
| `LadderLeadTaken` | Player: "You're #1 in the team" · GK: "You're the #1 goalkeeper" · ATK: "You're the #1 attacker" · Pair: "You and {partner} are the #1 duo" |
| `LadderLeadLost` | "{Subject} took the #1 {category} spot from you" |
| `PeakElo` | "New personal best: {value} ELO" |
| `WinStreak` | "{value} wins in a row" |
| `MatchMilestone` | "That was your {value}th match" |
| `NemesisBeaten` | "You finally beat {subject}" |

| Category | Label · hint |
|---|---|
| `Matches` | "Matches" · "When someone records a match you played in" |
| `Chat` | "Chat" · "Mentions and reactions to your messages" |
| `Seasons` | "Seasons" · "Season starts, endings, your result and awards" |
| `Rankings` | "Rankings" · "When the #1 spots change" |
| `Milestones` | "Milestones" · "Personal bests, streaks and match milestones" |

---

## 11. Security & validation

- **Recipients are computed server-side, never passed in.** No command takes a recipient list.
- **Reads and preference writes are owner-scoped**: `GetNotificationsQuery`,
  `MarkNotificationReadCommand`, `GetNotificationPreferencesQuery` and
  `SetNotificationPreferenceCommand` all filter on `IUserContext.UserId` and never take a user id
  parameter. Marking someone else's row read, or changing their preferences, is not expressible.
- **`SetNotificationPreferenceCommand` re-verifies** that the caller has a claimed player in the
  target team — a preference row for a team you are not in is meaningless and would leak team
  existence by id probing.
- **No new authorization surface elsewhere**: notifications are written only by handlers that
  already authorized the underlying action (match creation, chat send, season close). The
  aftermath and ladder-refresh commands are **system actions with no actor check** — the same
  documented stance as `CloseSeasonCommand`, and for the same reason: they are dispatched by a
  post-commit bridge on behalf of a write that was already authorized, and they take no input a
  caller could steer. They still run inside the triggering dispatch's scope, so `IUserContext` is
  populated; they simply do not consult it.
- **No leakage across teams**: every row carries `TeamId` and is only ever created for users with
  a claimed player in that team. A notification can therefore never reveal a team you are not in.
- **XSS**: notification text is composed in Web from a fixed template plus untrusted names
  (player names, team names, emoji), rendered through Blazor's auto-encoding as ordinary
  component content — **never** `MarkupString`. The message body is *not* copied into the
  notification at all (unlike the chat banner preview): the row links to the message instead.
- **The `DedupKey` is server-composed** from ids and enum values, never from user input.

---

## 12. Constants (initial, tunable)

A `Constants.Notifications` block, mirroring `Constants.Chat`:

| Constant | Value | Notes |
|---|---|---|
| `PageSize` | 20 | Feed page / "load more". Same number as `Pagination.DefaultPageSize`, kept as its own constant for the same reason `Chat.HistoryPageSize` is: a feed page is tuned against its own surface, not against table pagination. Fetch `PageSize + 1` to detect has-more (§9.1). |
| `WinStreakThresholds` | 3, 5, 10, then every 5 | §6.5 |
| `MatchMilestones` | 10, 25, 50, 100, then every 100 | §6.5 |
| `BadgeCap` | 99 | Displays as `99+`, like chat |
| `DefaultChannels` | `NotificationChannel.InApp` | The "no row stored" default (§8.2) |

The ladder eligibility thresholds are **not** new constants — they reuse
`TimeThresholds.MinGamesForPositionBadge` and `MinGamesForPartnerStats` so the bell and the
rankings tables can't disagree about who is even on a ladder.

---

## 13. Phase 2 — browser / OS notifications (designed, not built)

Deferred from v1 by decision. This section exists so v1 doesn't paint it into a corner — and §8's
`Channels` flags column is the main piece of that groundwork.

### 13.1 Why it is a real project on this stack

Blazor Server holds a circuit only while a tab is open. Everything in v1 reaches you only while
you are looking. Reaching a **closed tab or a phone** means the Push API, which needs a service
worker, a push service subscription, VAPID keys and a server that can send outbound HTTPS at
notification time. The PWA scaffold (installable shell, `service-worker.js`, manifest) is already
in place, which is the hard prerequisite on iOS.

A cheaper intermediate step exists and is worth doing first: the **local `Notification` API**
fired from the open circuit when the tab is backgrounded. No VAPID, no subscriptions, no new
tables, no background sender — `chat.js` already reports visibility/focus into the dock, so the
bell knows when it's safe to pop one. It covers the common office case (the app is open in a
background tab) and nothing else. It can honour the `Push` preference bit without any of the
infrastructure below.

### 13.2 Web Push design sketch

- **`PushSubscription` entity**: `Id`, `UserId` (FK, Cascade), `Endpoint` (maxlen 512, unique),
  `P256dh`, `Auth`, `UserAgent?`, `CreatedAt`, `LastSeenAt`, `FailureCount`. A 404/410 from the
  push service means the subscription is dead — delete it.
- **Keys**: VAPID pair in configuration (`Push:PublicKey`, `Push:PrivateKey`, `Push:Subject`),
  public key handed to JS. Private key is an App Service setting, never in the repo.
- **Client**: an "Enable browser notifications" control (permission **must** be requested from a
  user gesture) → `navigator.serviceWorker.ready` →
  `pushManager.subscribe({ userVisibleOnly: true, applicationServerKey })` → POST to a minimal
  API endpoint → `SavePushSubscriptionCommand`. Natural home: the top of the Account page's
  Notifications panel, above the per-team grid — permission is a per-browser/device concern, the
  per-team grid is per-account.
- **Preferences**: no new table. The Account grid grows a second column per category
  (bell / push), writing the `Push` bit of the same `Channels` value (§8.2). Push defaults to
  **off** for every category — the opposite of in-app — so granting permission never opens a
  firehose.
- **Service worker**: add `push` and `notificationclick` handlers. ⚠ The worker precaches a shell
  keyed by `const CACHE = 'fotbalek-shell-v2'` and **only reinstalls when its own bytes change** —
  the file already documents this; bump the version with the change.
- **Sender**: `IPushSender` in Application, implemented in Infrastructure over a library.
  Candidates: `Lib.Net.Http.WebPush` (MIT) or `WebPush` — **verify the licence before adopting**
  (the repo is deliberate about this; see the MediatR pin rationale in architecture.md §1). Do
  not hand-roll VAPID JWT + AES128GCM.
- **Outbound work must not block a request**: a `Channel<PushJob>` drained by a
  `BackgroundService`. This would be the app's **first hosted service** — a genuine new moving
  part, and the main reason this is phase 2.
- **Send gating** (the second half of the spam answer, after preferences): push only when the
  recipient has **no browser-active session**. `PresenceTracker` knows who has a live circuit but
  not who is *looking*; the dock already computes browser-active via `chat.js` → extend
  `PresenceTracker` with an active flag fed from there, and push only to users who are offline or
  present-but-not-active.
- **Quiet hours**: a per-user window; the only preference concept §8 does *not* already cover.
- **Collapsing**: `tag` per (team, category) with `renotify: false`, so ten reactions don't stack
  ten OS banners.
- **iOS**: Web Push requires the PWA to be **installed to the home screen** (16.4+) — it does not
  work in a Safari tab. The install prompt story therefore becomes part of this phase.
- Scale-out is *not* a blocker here: push is stateless outbound HTTP, unlike the in-memory
  notifier (§14).

---

## 14. Out of scope (v1)

- **Browser / OS notifications** — §13.
- **Quiet hours** — the one preference axis v1 skips; it only matters once something can wake
  your phone (§13.2).
- **Global (all-team) preference defaults** — the `TeamId == null` tier is designed for and
  reserved, not built (§8.2, §16).
- **Team & roster events** — "someone joined your team", "a player was claimed", "you were made
  captain", "a match you played in was deleted". All cheap, all deliberately deferred (§15).
- **A bell on nav-less pages** — `/account`, `/create`, `/join` and `/team/{code}/claim` render
  `MainLayout` (the router default; only the team pages carry `@layout TeamLayout`), so they have no
  navbar and get no bell (§9.4). Accepted: they are transient account/onboarding screens — note that
  the claim page is *inside* a team URL but deliberately outside the team chrome, because
  `TeamLayout` redirects there precisely when the user has no claimed player and therefore cannot
  receive notifications at all (§1). `/` is covered by a per-team badge on the teams list.
- **Live-game notifications** — the live-game spec explicitly has none; discovery stays the
  header badge.
- **Loss-streak notifications** — §6.5; excluded by decision, revisit once the feature has been
  lived with (§16).
- **Jump to a specific chat message** — chat notifications open the dock on the right team only.
- **Digests / email** — no outbound mail exists in the app at all.
- **Multi-instance scale-out** — `NotificationNotifier` is in-process, same single-server caveat
  as `ChatNotifier`, `PresenceTracker` and `GameRoomManager`. Rows are persisted, so a restart
  loses nothing but live fan-out to circuits already connected.
- **Automated tests** — the solution has no test project; the writer, the recipient resolver, the
  preference resolution, the ladder helpers and the milestone rules are all pure or
  `IAppDbContext`-only so they are testable later without refactoring.

---

## 15. Future extensions (kept compatible)

1. **Quick mute from a feed row** — "stop telling me about {category} in {team}" from the row's
   `⋯` menu; writes the same preference row (§8.6).
2. **Global preference defaults** — the reserved `TeamId == null` tier, resolved under the
   per-team row.
3. **Team & roster events** — new member joined, player claimed, captain handover, match deleted
   ("your ELO was reverted"). Pure additions: a new enum value, its category, a
   `Describe`/`Target` arm, one `INotificationWriter.AddAsync` call in the relevant handler.
4. **Reaction aggregation** — "3 people reacted to your message": collapse by `ChatMessageId`
   when unread, replacing the per-reactor rows.
5. **Award collapse** — one "you won 3 season awards" row instead of three.
6. **Jump-to-message** — needs `ChatConversation` to support seeking to a message id
   (load the page containing it, then scroll); chat.md §4.7's pagination is the thing to extend.
7. **"Season ends tomorrow"** — same lazy hook and same single-column guard as §5.4
   (`EndsSoonAnnouncedAt`), a nudge to get matches in before the ladder freezes.
8. **Loss streaks / lighthearted lowlights** — deferred from v1 (§16); purely additive when wanted.
9. **Incremental ladder aggregates** — if §6.4's full load ever gets slow.
10. **Pruning** — retention window on read rows (§7.4).
11. **Splitting the `Chat` category** into mentions vs reactions, if one toggle proves too coarse.
    Unlike every other addition here this one is **not** free: existing `Chat` preference rows
    must fan out into two rows carrying the same `Channels` value. Mechanical, but it is a data
    migration — worth knowing before someone assumes categories are cheap to re-cut (§8.1).
12. **One mention matcher, not three** — fold `ChatDock.MentionsMe`'s substring test onto the shared
    scanner so the banner's "mentioned you" wording can never disagree with the bell (§5.2). Needs
    mention data on `ChatMessagePostedEvent`/`ChatMessageDto`, which is why it is not v1.
13. **Announce the first lead in a new scope** — make `LadderLeader.PlayerId` nullable so
    "evaluated, nobody eligible" becomes distinct from "never evaluated", and seed the null rows when
    a season is created (§3.4). Today the first match in a new season writes the snapshot silently.
14. **Web Push** — §13, the big one.

---

## 16. Decision log

Settled with the user (2026-07-29):

| Topic | Decision |
|---|---|
| v1 triggers | **Core set + personal milestones**: match recorded with you, chat mention, chat reaction, season started, season ended (+ final rank and awards), and the milestone set (peak ELO, win streaks, match-count milestones, nemesis beaten). Team/roster events and live-game notifications are **out** (§14/§15). |
| Rank notifications | **#1 changes only**, across **four ladders** — solo, duo, goalkeeper, attacker (= the four award categories = the four Rankings tables). Both the new leader and the dethroned one are told (§6). |
| Ladder scope | Seasonal when the match was seasonal, all-time otherwise — one ladder set per match (§6.2). |
| Browser / OS notifications | **Not in v1.** Fully designed as phase 2 (§13), with the local `Notification` API as a cheap intermediate step. |
| Placement | **Bell in the `TeamLayout` navbar, left of the account menu**, opening an anchored `.dropdown-menu` (§9.1). Chosen after reviewing the convention: the top-right-beside-the-avatar bell is what comparable apps ship, while a floating bottom-right circle reads as a support/chat widget. *Supersedes* an earlier pick of a floating bell beside the chat launcher — that framing over-weighted the loss of coverage on `MainLayout` pages, which are a hero landing plus one-off onboarding/account screens. Home keeps a per-team badge (§9.4). |
| **Preferences** | **Per team**, so a user can take everything from one team and only the ladder changes from another. Configured in a **Notifications panel on the Account page**, reached from a **⚙ cog in the bell panel** (§8). |
| **Feed scope** | **Account-wide, cross-team.** A notification belongs to a *user*, not a team; the feed shows every team you are in, regardless of which team's navbar the bell is rendered in (§1, §9.3). `TeamId` is a label, a target and the preference axis — not a partition. Precedent: GitHub's bell inside a repo shows your whole account's notifications. Rows carry a team chip whenever the user has more than one team, which is what keeps a cross-team list legible from inside one team. |

Settled with the user during the revision-4 verification pass (2026-07-29):

| Topic | Decision |
|---|---|
| **Aftermath execution** | **Synchronous, post-commit, as a nested dispatch** — the shape `SeasonCreatedPastDueEventHandler` already uses (§6.3). Chosen over a fire-and-forget `Task.Run`: nothing is lost on shutdown, the recorder's own circuit sees the rows inside the round trip, and there is no principal to synthesize (a bridge handler has `IUserContext`, not a `ClaimsPrincipal`). The cost is one team-history load on the "Record match" round trip; the background task stays documented as the first escape hatch if that ever measures badly (§6.4). |
| **All-time snapshot on a seasonal match** | **Refresh both scopes, announce one** (§6.2). A seasonal match also moves all-time ELO, so evaluating only the season's ladders would leave the all-time snapshot stale and let a later off-season match announce a change that happened weeks ago — or never. Both scopes come out of the same in-memory load, so the fix is a flag, not a second pass, and the announce volume the user asked for is unchanged. |
| **Pair-ladder eligibility** | **Exclude inactive members in both scopes** (§6.1). The all-time pair table is the only one of the eight that does not filter `IsActive`; the shared helper filters it and `GetPairRankingsQuery` adopts that, so the bell can never announce that you lost the #1 duo spot to a pair that no longer exists. Accepted side effect: that table stops listing pairs with a deactivated member. |
| **`NemesisBeaten`** | **The single worst opponent**, matching what the Stats page already calls your nemesis — not "any opponent who beats you more than half the time" (§6.5). Rarer, so it stays special, and it cannot disagree with the page. Requires extracting `NemesisStat`'s per-player math into a shared rule, with a deterministic tie-break added. |

Decided while drafting (call these out if you disagree):

| Topic | Decision & why |
|---|---|
| Preference granularity | **Five categories, not twelve types** (§8.1) — twelve toggles per team is a page nobody reads, and the five map onto how people describe the feature. |
| Preference storage | **Sparse override rows**, default = everything on (§8.2). No seeding on join, nothing to keep in sync, and an empty table is a valid initial state. |
| Preference enforcement | **At write time** — muted categories are never inserted (§8.3). Keeps the unread count honest and avoids dead rows; the accepted cost is that re-enabling shows nothing retroactively. |
| Channel modelling | `Channels` is a **flags enum** (`InApp` / `Push`), not a bool — phase 2's push toggle is a second bit on the same row, with no migration and no second table (§8.2/§13.2). |
| Read model | **Two tiers, per row** — `SeenAt` (set when the panel opens) drives the badge, `ReadAt` (set on click) drives the row's own unread styling; no watermark, because a feed is read out of order (§7.1). Chosen over a single `ReadAt` because `MatchRecorded` fires on every match, so a badge that only cleared row-by-row would realistically never reach zero — and a badge that never reaches zero is one people stop reading. `ReadAt` still answers "what have I actually looked at". |
| Write timing | Notifications are written **inside the acting command's transaction**; only *delivery* is post-commit. A notification for a rolled-back action would be a lie (§4.1). |
| Ladder detection | **Persisted `LadderLeader` snapshot**, compared after each match — avoids a before/after double computation, and gives a natural silent-first-write backfill guard (§6.3). |
| Aftermath execution | **Outside the match transaction, serialized by the existing per-team timeline lock** — the ladders aggregate the team's whole history and must not sit inside the match transaction, which holds the season row lock (§6.3). *Whether* it also leaves the request has since been settled the other way: see the revision-4 table above. |
| Arrival cue | **Badge + a one-shot bell shake** — no toast (against `ToastService`'s own documented rule) and no banner surface; chat already owns that corner (§9.1). |
| Panel mechanics | A plain `ui.js` dropdown, **not** a Blazor-owned open flag — `ui.js` keeps open state in the DOM specifically so a re-render can't close a live-updating menu, which is exactly what an arriving-notification feed needs. `data-keep-open` on "Mark all"/"Load more" (§9.1). |
| Settings feedback | Per-toggle immediate save, **no toast** — the switch is its own feedback, which is `ToastService`'s documented "neither" case; failures flip back and show inline (§8.5). |
| Chat overlap | A mention raises chat's transient banner **and** a permanent bell row; reading the chat past that message marks the bell row read (§7.3). Muting the `Chat` category silences the bell row only — chat's own badges and banners are chat's feature. |
| Mention matching | Moves into a shared `MentionScanner` in Application, and `ChatMessageView` is refactored onto it — two matchers would drift and produce pills without notifications (§5.2). |
| Presentation | All wording, icons and targets in `Web/Services/NotificationPresentation`, behind CS8509-guarded switches. Application stores the enums and the ids only (§10, repo convention). |
| Subject FKs | Real nullable FKs with `Restrict` + explicit cleanup in the two hard-delete commands, rather than untyped id columns — referential integrity is worth one `ExecuteDelete` per delete path (§3.1). |

Closing out the drafting questions (2026-07-29) — all six resolved:

| Question | Resolution & why |
|---|---|
| Loss streaks as a milestone | **Excluded from v1** (§6.5). The reversibility is asymmetric: adding it later is one enum value, one presentation arm and one rule, while getting it wrong the other way leaves a user whose only remedy is muting all of `Milestones` — losing peak-ELO and win-streaks with it. |
| Global (all-team) preference defaults | **Deferred** (§8.2). At one to three teams a global tier saves nobody anything. The `TeamId == null` column is reserved, so adding it later is a query change plus one settings section — no migration. It earns its precedence rule only at four-plus teams. |
| Category grouping | **Keep the five** (§8.1). The point of five was a settings page people read; a sixth toggle for a distinction most users won't exercise is a bad trade. Noted as the one non-free future change, because re-cutting categories means migrating stored rows (§15 item 11). |
| Badge behaviour | **Two-tier `SeenAt`/`ReadAt`** — see the read-model row above. |
| Season-award volume | **Left as-is** (§5.5). Season close fires a few times a year; five rows once a quarter is not a volume problem, and "you won three things" reading as a burst is the point. |
| `MatchRecorded` volume cap | **No cap** (§5.1). A cap would drop information people want — it is the audit trail for why an ELO moved — and it already only fires for matches *someone else* entered. The complaint hiding behind "volume" was the badge never clearing, which the two-tier model fixes at the badge instead of by discarding rows. |

---

## 17. Verification log

_Earlier open questions were resolved 2026-07-29, all in §16: **feed scope** (account-wide,
cross-team); **loss streaks** (excluded); **global preference defaults** (deferred, column reserved);
**category grouping** (five, keep); **badge behaviour** (two-tier `SeenAt`/`ReadAt`);
**season-award volume** (left as-is); **`MatchRecorded` cap** (none). Four more were settled during
this pass — see §16's third table._

**No open questions remain.** The spec is ready to be turned into an implementation plan.

### 17.1 What was wrong, and is now fixed

Every one of these was a claim about existing code that did not survive reading it:

| § | Was | Is |
|---|---|---|
| §6.1 | "the ladders' own deterministic tie-breaks already resolve a tie" | Three of the six ranking queries have **no** final tie-break; the all-time solo ladder orders on ELO alone. Untreated, a tie flip-flops the snapshot and fires a took/lost pair on every match. The shared helper adds the chain and those three queries adopt it. |
| §6.1 | "the four ladders" = four queries | Four categories × two scopes = **eight tables across six queries**; the three seasonal ones were missing from the spec entirely, and they order differently from their all-time twins. |
| §6.2 | a seasonal match only changes the season's ladders | It changes all-time ELO too — the seasonal pass is *additional*. The all-time snapshot has to be refreshed silently or it goes stale (§16). |
| §7.3 | hook the read-clearing on `MarkChatReadCommandHandler` | The watermark advance lives in the shared `ChatReadStateAdvancer`, which `SendChatMessageCommand` also calls. Hooked on the command, answering a mention in chat would leave the bell row bold. |
| §5.2 | two mention matchers | **Three** call sites: `ChatMessageView.ComputeSegments`, `ChatDock.MentionsMe` (a bare substring test) and the new scanner. v1 leaves the banner one alone, knowingly, and says so. |
| §4.1/§5.3 | the writer adds rows; delivery is post-commit | The collector flushes regardless of whether the handler ever saved, so `AddAsync` without a following `SaveChanges` publishes events for rows that do not exist. `ToggleChatReactionCommandHandler` has no safe seam and needed a spelled-out one. |
| §7.2/§9.3 | "opening the panel marks everything seen" + "open state is not Blazor's problem" | Those two cannot both be true as written: `ui.js` never tells .NET that the menu opened. Replaced with an idempotent trigger handler that does not care about direction. |
| §5.2 | set `ChatUiState.IsOpen` to open the dock | `ChatDock` is a sibling component and `ChatUiState` raises no event for open state, so nothing would re-render. Needs a small `RequestOpen`/`OpenRequested` addition. |
| §8.5 | add an exported scroll helper to `ui.js` | `ui.js` is a closed IIFE with no export surface; `app.js` is where every `window.*` interop helper lives. |
| §9.1 | panel sizing in `nav.css` beside the presence-menu rules | `.presence-menu` is in `team-navbar.css`; `nav.css` has no `.dropdown-menu` rules at all. |
| §9.1 | "the actor's avatar when there is one" | `Player.AvatarId` is a non-nullable `int` defaulting to 1. The real split is actor rows vs system rows. |
| §2 | "rank is never stored" | Live rank is never stored; `SeasonPlayerResult.FinalRank` **is**, at close — which is what §5.5 reads. |
| §5.4 | announce a start whenever `StartsAt <= now` at creation | A season created entirely in the past closes in the same round trip, so that would deliver "has started" and "ended" together. Guarded. |
| §8.4 | `GetMembershipOverviewQuery` is the claimed-player rule | It returns every membership with `MyPlayer` possibly null; callers filter (`ChatDock` does). |
| code map | — | `IAppDbContext` needs the three new `DbSet`s; nothing compiles without it and it was missing. |

Also newly specified rather than corrected: the `(de)activation` ladder-refresh gap (§6.3), folding
the two lazy-hook queries into one (§5.4), `SeasonCloseProcedure` returning what it froze (§5.5),
`data-keep-open` losing to `.dropdown-item` (§9.1), `LoadMoreButton`'s `Remaining` versus a keyset
cursor (§9.1), the `CS8524` pragma that accompanies the `CS8509` guard (§10), and `NemesisRule`'s
extraction and tie-break (§6.5).

### 17.2 What was checked and holds

Confirmed against `src/` rather than assumed: the `IEventCollector` → post-commit flush →
bridge → notifier path and that a **post-commit nested dispatch gets its own transaction**
(`HasActiveTransaction` reads `Database.CurrentTransaction`, which EF clears on commit — the
mechanism `SeasonCreatedPastDueEventHandler` already depends on, and what lets §6.3 take an
`IDbLocks` lock); `ScopedDispatcher` being a singleton taking a `ClaimsPrincipal?`; `IDbLocks`'
two Transaction-owned locks; the lazy season-close hook and its call sites in `CurrentTeamProvider`;
`SeasonCloseProcedure`'s frozen ranks, award categories, pair `PartnerPlayerId` and the 10-match
award threshold; `CreateMatchCommandHandler` having the four player ids, the team and the season;
`DeleteMatchCommand`/`DeleteSeasonCommand` being the only hard-delete paths that touch these FKs;
`MatchRules`' deletion window; every `Constants` value quoted (`MinGamesForPositionBadge` 5,
`MinGamesForPartnerStats` 3, `MinHeadToHeadGames` 4, `AwardCategories`, `MaxReactionEmojiLength`);
`ui.js`'s dropdown machinery, `data-keep-open`, Escape and outside-click handling; `TeamLayout`'s
navbar actions group being the right slot and staying visible on mobile (`order-2`/`ms-auto`);
`MainLayout` being the router default, so the nav-less pages really are nav-less; Home's live
per-team chat badges coming from `ChatUiState` + `ChatDock`; `.form-switch` existing in `form.css`;
`ToastService`'s toast/inline/neither rule, quoted correctly; `StatDisplay`'s CS8509 pattern and
`WarningsAsErrors` in `Directory.Build.props`; Tailwind's `@source` globs already covering
`Web/Services/*.cs`; `PlayerRules`' per-team name uniqueness (which is what makes a mention span's
`PlayerId` well-defined); the four target routes; and that the app has **no** hosted service or
scheduler.

### 17.3 Still a bet

The places where the design is a judgement call rather than a deduction — worth re-testing against
reality once it ships, not blockers:

- the `MatchRecorded` firing rate in a team that plays daily (§5.1), now that the badge model no
  longer punishes volume;
- whether one `Chat` toggle is coarse enough to annoy (§8.1) — the one future change that costs
  a data migration (§15 item 11);
- the aftermath run's cost, now that it sits on the "Record match" round trip (§6.4) — measure
  before optimizing, and §6.4 names the escape hatches in order;
- the `data-toggle="dropdown"` + `@onclick` pairing (§7.2), the one mechanism in this spec with no
  existing precedent in the codebase to copy;
- whether the silent first evaluation per new season (§3.4) reads as a missing feature rather than
  restraint.

### 17.4 Revision 5 (2026-07-29) — independent re-audit

Four parallel audits re-verified every existing-code claim from scratch. The revision-4 findings
all held (the six ranking queries and their exact ordering chains, the pair-table `IsActive`
exception, `ChatReadStateAdvancer`'s two callers and its non-member/early-return placement, the
toggle handler's swallowed `DbUpdateException` with no save after it, `ui.js`'s selector verbatim
and the absence of any `data-toggle` + `@onclick` pairing, the `TeamLayout` slot, the nav-less
pages, `LoadMoreButton`'s `[EditorRequired] Remaining` with no attribute splat, the CSS and
Tailwind-glob claims, `Player.AvatarId`, the no-hosted-service claim, and the hard-delete
inventory — exactly `Match`, `Season`, `SeasonPlayer`, `ChatMessageReaction`).

What the re-audit found wrong or missing, all fixed above:

| § | Was | Is |
|---|---|---|
| §5.5 | notifications written in `CloseSeasonCommandHandler` only | `SeasonCloseProcedure.CloseAsync` has a **second caller**, `EndSeasonNowCommand` (captain ends early). Both callers write, or an early-ended season finishes in silence. |
| §6.3 | matches change only via create/delete | `UpdateSeasonEndsAtCommand` (shrink) unassigns tail matches and **replays the seasonal ladder** via `SeasonLadderReplay` — a third `SeasonPlayer.Elo` writer. It gets the same silent refresh as (de)activation. |
| §5.4 | the announce command's own re-check was the whole story | The lazy announce **collides with the lazy close** when a season runs unvisited start-to-end: close runs first, the re-check suppresses the start announcement, and the unannounced *lookup* must filter `ClosedAt == null` or the suppressed season is re-dispatched on every page load forever. |
| §3.6 | backfill stamped every existing season | It would also stamp seasons **scheduled to start after deployment**, permanently muting them. The backfill adds `AND StartsAt <= SYSDATETIMEOFFSET()`. |
| §6.3 | handler derives scope "from the match's own `SeasonId`" | On the delete path the match row is gone. `SeasonId` now rides the command, which also covers the pending-close season the "active season" probe would miss. |
| §6.3 | `MatchRecordedBridge` in Web | The precedent (`SeasonCreatedPastDueEventHandler`) lives in **Application**, beside its command, and the dispatch has no Web dependency. Moved. |
| §4.2 | "both exclude the actor", mechanism unstated | Actor = `ActorUserId` on the draft. The aftermath's drafts set **no** actor at all — otherwise the recorder (the common self-recorder case) would never receive their own lead and milestone rows. |
| §9.1 | lead taken/lost listed among the actor rows | §3.2 gives them no `ActorPlayerId`; they are system rows and take the per-type icon. |
| §5.2 | three mention matchers | **Four** — the composer autocomplete (`ChatConversation.ComputeMentionState`) is its own again (active-only, word-boundary, prefix search). It suggests rather than decides, so it is not a drift risk; recorded so nobody "unifies" it by mistake. |
| §5.2 | scanner rule stated without the boundary detail | `ComputeSegments` has **no word-boundary requirement** before the `@` (`mail.foo@Alice` pills); the scanner inherits that explicitly. |
| §5.2 | dock handler "the same body as `OpenFromBanner`" | `OpenFromBanner` skips `StateHasChanged` (it is the component's own `@onclick`); the cross-component subscription must call it. |
| §6.3 | deletion window "and no participant has played since" | Precisely: 24 h from `CreatedAt`, never a **closed season's** match, no participant match with a higher id. Conclusion unchanged. |
| §6.5 | "call straight into `PeakEloStat`" | Its `Compute` is `protected` and `Calculate` returns team-wide leaderboard holders, not a per-player peak — compute the one-expression rule inline. `StreakComputer` is directly callable (`internal`, same assembly). |
| §10 | "following `StatPresentation`: a static class" | The static class is `StatDisplay`; `StatPresentation` is the record it returns. |
| §4.4 | "same shape as `ChatNotifier`" | `ChatNotifier` keys every event on `teamId`, not `userId`; the shared part is the filtering-subscriber pattern. |
| §3.1 | "the repo's user-FK convention" = Restrict | The convention splits: content rows Restrict, per-user state rows (`ChatReadState`) **cascade** — which is exactly the Notification/NotificationPreference split already specced. |

Also noted, no spec change needed: `CreateMatchCommandHandler` takes `LockSeasonRowAsync` only on
the seasonal path and no lock off-season (the aftermath's team-timeline lock is the serialization
either way); the season lock's real name is `LockSeasonRowAsync`; `NemesisStat`'s team-wide
narrowing keeps epsilon co-holders rather than `.First()` — the untie-broken `.First()` the spec
targets is the per-player worst-opponent pick, which is the one `NemesisRule` extracts;
`CloseAsync` also writes `SeasonPair` rows and stamps `ClosedAt`/`EndsAt ??= now`; the closed-season
read paths gate on frozen `FinalRank`, not live `IsActive`, which never intersects the aftermath
(it only ever evaluates a season a match can still land in); and a shared
`Components/Shared/Dropdown.razor` wrapper exists as an alternative to hand-rolled dropdown markup
(§9.1).
