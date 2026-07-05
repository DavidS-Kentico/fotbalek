# Seasons Feature - Specification

**Status**: Reviewed against the codebase four times (last: 2026-07-03 — independent claim-by-claim verification; timezone policy revised to local-time-at-UI-boundary, complete `DateTime.Today` site list, multi-season lazy close, award-threshold constants). Ready for implementation. Amended 2026-07-04 (post-implementation): season names are unique per team (§2.1, §4.1, §4.3). Amended 2026-07-05: season start/end admin input is a local **date-time** (midnight default), not date-only — enables mid-day boundaries and intra-day seasons (§2.1); period displays show the time when it is not midnight.

## 1. Overview

A **Season** is a named, per-team time period that groups matches. Each season has its own ELO ladder — everyone starts fresh, champions and leaderboards are decided by seasonal ELO. When a season closes, its final standings and awards (top players, goalkeepers, attackers, pairs) are computed once and stored in strongly-typed tables, so they are viewable forever without re-aggregation. Awards become permanent achievements on player profiles.

**Naming**: "Season" is kept. Alternatives considered — *League* (implies separate competitions), *Split* (esports jargon), *Tournament* (implies bracket/elimination).

**Scope note**: consistent with the PoC spirit of the project — no background jobs, no caching layers, lazy evaluation where possible.

---

## 2. Data Model

### 2.1 Season (new entity)

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | int | PK, auto-increment | |
| TeamId | int | FK → Team.Id, required, indexed, ON DELETE CASCADE | Seasons are per-team |
| Name | string(100) | required, **unique per team** (case-insensitive, trimmed) | e.g. "Season 1", "Spring 2026". The name is the human identifier in season selectors, match chips and trophy cases — surfaces that don't show the period — hence uniqueness |
| Description | string(500) | optional | Shown on Dashboard header and season detail |
| StartsAt | DateTimeOffset | required | Inclusive start. May be in the past (see §4.1) or future (scheduled season) |
| EndsAt | DateTimeOffset | nullable | Exclusive end. `null` = open-ended. Matches played after this moment are **off-season**, not part of any season (§5) |
| ClosedAt | DateTimeOffset | nullable | When the season was closed and results frozen. `null` = not yet closed |
| CreatedAt | DateTimeOffset | required, default: UtcNow | |

**Definitions** (used throughout this spec):
- **Closed**: `ClosedAt != null`. Results are frozen and immutable.
- **Active**: `ClosedAt == null && StartsAt <= now && (EndsAt == null || now < EndsAt)` — the season currently accepting matches. At most one exists per team (guaranteed by the non-overlap invariant). The `EndsAt` condition is essential: without it, a season past its end date but not yet lazily closed would still count as "active" — and could coexist with a newer season whose `StartsAt` has already arrived, breaking the at-most-one guarantee and the §5.1 assignment rule.
- **Ended, pending close**: `ClosedAt == null && EndsAt <= now` — past its end date, waiting for the lazy close (§4.2). Not active: it accepts no matches.
- **Scheduled**: `ClosedAt == null && StartsAt > now` — created ahead of time; nothing is active about it until `StartsAt` arrives.

**Invariants** (enforced in `SeasonService`, per team):
- Season **names are unique per team** (case-insensitive, trimmed) — checked on create and on rename (excluding the season itself). The create check runs under the same per-team application lock as the overlap check (§4.1); the rename check is plain check-then-save (a concurrent duplicate rename is accepted at PoC scale, mirroring the player-name check).
- Season periods **must not overlap**: `[StartsAt, EndsAt)` intervals of a team's seasons are disjoint. An open-ended season extends to infinity for this check. **This is the only rule constraining the period**; its consequences:
  - While a season is **open-ended**, no later season can be created (its interval reaches infinity) — the admin must first end it or set its `EndsAt`. UI hint: *"End the current season (or set its end date) first."* There is still **no** implicit "auto-close the active season" flow.
  - A season **entirely in the past** (the backfill/import use case, §4.1) can be created at any time, even while another season is active or scheduled.
- Consequence: at most one season's period contains `now`, hence at most one **active** season. Multiple **scheduled** seasons are permitted (harmless — disjoint future periods); the UI treats the nearest upcoming one as "next".
- `EndsAt > StartsAt` when set.

**Timezone**: **entities and logic use `DateTimeOffset` exclusively; local time exists only at the UI boundary.** `StartsAt`/`EndsAt` are instants (`DateTimeOffset`) and every service-level comparison runs against `DateTimeOffset.UtcNow` — matching the existing schema, where all timestamps are already `DateTimeOffset`. For display and date interpretation the UI uses the **user's browser timezone**, fetched once per circuit via a small JS interop call (`Intl.DateTimeFormat().resolvedOptions().timeZone`, in `OnAfterRenderAsync` of the team layout — interop is unavailable during prerender) and cached in a scoped `TimeZoneService`; until it resolves, fall back to UTC. Server-local time (`DateTime.Today` / `DateTime.Now`) is banned — deployment containers typically run in UTC, so "server local" is meaningless. Consequences:
- Admin input for season start/end is a **local date-time** (`datetime-local`, defaulting to midnight — day-granularity seasons stay the simple default) in the user's timezone, converted to a `DateTimeOffset` instant on save. Mid-day boundaries are first-class: a season can end at 5 PM and the next one start at 6 PM the same day. Season periods display in local time; the wall-clock time is shown whenever it is not midnight, so a mid-day boundary never silently looks like a whole-day one.
- "Today" / "This Week" filters and match-day grouping are **local days**: convert `PlayedAt` to the user's timezone before taking the date.
- As part of this feature, **every** `DateTime.Today` site switches to the local-day helper (`DateFilterHelper` becomes parameterized by the user's `TimeZoneInfo` / local today): `DateFilterHelper` itself (both uses, incl. `GetPeriodDescription`), the custom-date initialization in the Match History and Stats pages, `TimePeriodFilter`'s date-input `max` attribute and `GetMaxEndDate`, Stats' `GetFilterDateRange` fallback, and Match History's `FormatGroup` day headers. After this feature no code touches `DateTime.Today`/`DateTime.Now`; entities and logic never leave `DateTimeOffset`.

### 2.2 SeasonPlayer (new entity)

One row per player who participated in the season — created lazily on the player's first seasonal match. This doubles as the explicit list of **all players that played the season**.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | int | PK, auto-increment | |
| SeasonId | int | FK → Season.Id, required, indexed, ON DELETE CASCADE | |
| PlayerId | int | FK → Player.Id, required, ON DELETE RESTRICT | Unique together with SeasonId |
| Elo | int | required, default: 1000 | **Seasonal ELO** — updated only by this season's matches |

This row is the **live ladder** — hot state, updated on every seasonal match, deleted when the player's last season match is removed (§4.3, §5.2). It deliberately carries nothing else: everything written at close lives in `SeasonPlayerResult` (§2.3), so live state and frozen results never share a row. While the season is active, standings (rank, W/L, streaks, position figures) are computed on the fly from season matches, exactly like today's rankings.

`Elo` naturally does double duty: a closed season accepts no new matches (§5.1) and its matches cannot be deleted (§5.2), so at close the value simply stops changing and *is* the final seasonal ELO — no frozen copy needed.

**Deactivated players** (`Player.IsActive == false`): excluded from season standings, season stats, and awards (§8). Their `SeasonPlayer` row is kept (their matches happened and affected opponents' ELO), but they are hidden from standings while inactive, and if inactive at close their `SeasonPlayerResult.FinalRank` stays `null` (§2.3). Reactivating a player mid-season makes them reappear in live standings; reactivating after close changes nothing — frozen results stay frozen.

### 2.3 SeasonPlayerResult (new entity)

Frozen per-player results — one row per participant, **inserted once** inside the close transaction (§8) and never updated. Splitting this off `SeasonPlayer` makes the lifecycle structural instead of conventional: **result rows exist if and only if the season is closed**, every stat column is non-null by schema (no "valid only after close" nullable soup), immutability is a table-level property (the table is insert-only), and the shape is symmetric with the other close artifacts (`SeasonPair`, `SeasonAward`).

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| SeasonPlayerId | int | PK, FK → SeasonPlayer.Id, ON DELETE CASCADE | 1:1 with the ladder row (PK = FK) |
| FinalRank | int | **nullable** | The one meaningful null: player was **inactive at close** (§8) — excluded from frozen standings |
| Wins | int | required | Wins by score (§3) |
| Losses | int | required | |
| MatchesPlayed | int | required | |
| LongestWinStreak | int | required | Longest win streak within the season (score-based, chronological order) |
| LongestLossStreak | int | required | |
| GoalkeeperMatches | int | required | Matches played as goalkeeper |
| GoalsConcededAsGoalkeeper | int | required | Per-game = ÷ `GoalkeeperMatches` |
| AttackerMatches | int | required | |
| GoalsScoredAsAttacker | int | required | |

Win rate is derived (`Wins / MatchesPlayed`), final seasonal ELO is read from the ladder row (`SeasonPlayer.Elo`, §2.2) — closed seasons never aggregate again. Badge-style stats beyond these main figures are *not* frozen (§6.2/§6.3). The PK doubles as a second idempotency backstop for the lazy close (§4.2): a duplicate close attempt fails structurally on insert.

### 2.4 SeasonPair (new entity)

Frozen pair standings — rows are written **only at season close** (while the season is active, pair standings aggregate on the fly like today). One row per duo that played at least one match together in the season; render-time filtering applies the usual minimum (`MinGamesForPartnerStats`), so the stored data stays reusable if thresholds change.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | int | PK, auto-increment | |
| SeasonId | int | FK → Season.Id, required, indexed, ON DELETE CASCADE | |
| Player1Id | int | FK → Player.Id, required, ON DELETE RESTRICT | Convention: `Player1Id < Player2Id` |
| Player2Id | int | FK → Player.Id, required, ON DELETE RESTRICT | |
| MatchesTogether | int | required | |
| WinsTogether | int | required | Wins by score (§3) |

Unique index on `(SeasonId, Player1Id, Player2Id)`.

### 2.5 SeasonAward (new entity)

Permanent, stored achievements — **not** computed badges. Generated once at season close.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | int | PK, auto-increment | |
| SeasonId | int | FK → Season.Id, required, indexed, ON DELETE CASCADE | |
| PlayerId | int | FK → Player.Id, required, indexed, ON DELETE RESTRICT | Indexed for "show my trophies" on player detail |
| Category | string(20) | required, values: "Player", "Goalkeeper", "Attacker", "Pair" | |
| Rank | int | required, values: 1-3 | Gold / silver / bronze |
| PartnerPlayerId | int | nullable FK → Player.Id, ON DELETE RESTRICT | Only for Category = "Pair" — the teammate. Pair awards create **one row per member** so lookups by PlayerId stay trivial |

Unique index on `(SeasonId, Category, Rank, PlayerId)` — a backstop against duplicate award generation (see §8 idempotency).

Awards generated per season (v1): **Top 3 players** (by seasonal ELO, ≥ 10 matches played), **Top 3 goalkeepers** (fewest goals conceded per game as goalkeeper) and **Top 3 attackers** (most goals scored per game as attacker) — the **same goals-per-game metric the Rankings position tables use**, so the awards and the rankings always agree — and **Top 3 pairs** (duos by win rate together, position-agnostic, same metric as the pair rankings; both members receive the award).

**Eligibility**: awards are generated **only if the season has at least 10 matches in total** — below that, the season closes with frozen standings but no awards. Per-category thresholds are **the same as the rankings tables**, so the awards podium always matches what the Rankings page shows: position awards require **5 matches in that position** (`MinGamesForPositionBadge`), pair awards **3 matches together** (`MinGamesForPartnerStats`), and the Top-3-players category requires **at least 10 matches played** in the season. The Player podium is therefore the frozen standings order **filtered** to players with ≥ 10 matches — a standings winner (`FinalRank = 1`) with fewer than 10 matches wins no Player award, so the award champion and the standings leader **can disagree**; the season list (§7.1) prefers the award holder. Additionally the player must be **active at season close** (`Player.IsActive`); a pair is excluded if **either** member is inactive at close. If fewer than 3 candidates qualify, lower ranks are simply not awarded. The two 10-match minimums (season total, Top-3-players) are **new constants** (e.g. `Constants.Seasons.MinMatchesForAwards`, `MinMatchesForPlayerAward`) — do not reuse the coincidental `MinGamesForCarriedBadge = 10`, which is an unrelated badge threshold.

### 2.6 Match (changed)

| New field | Type | Constraints | Description |
|-----------|------|-------------|-------------|
| SeasonId | int | FK → Season.Id, **nullable**, indexed, **ON DELETE NO ACTION** (service-managed, see below) | `null` = off-season match |

**SQL Server constraint note**: the app runs on SQL Server, where `ON DELETE SET NULL` here would create a second cascade path onto `Match` (Team→Match CASCADE plus Team→Season→Match) and the migration would be rejected ("may cause cycles or multiple cascade paths"). The FK is therefore `NO ACTION` (`DeleteBehavior.ClientSetNull` in EF), and `SeasonService.Delete` nulls `Match.SeasonId` explicitly inside its transaction (§4.4) — which it must do anyway, since clearing the `MatchPlayer.SeasonElo*` columns can never be done by an FK.

**Team deletion caution** (latent — no team-delete feature exists in the app today): the FK triangle Team→Season (CASCADE), Team→Match (CASCADE), Match→Season (NO ACTION) means a raw `DELETE FROM Teams` may fail at runtime — cascade ordering across the two paths is not guaranteed, and a Season row cannot be deleted while Match rows still reference it (unverified against SQL Server; treat as a footgun). The new `RESTRICT` FKs to `Player` (`SeasonPlayer`, `SeasonPair`, `SeasonAward`) add to the same footgun — exactly as `MatchPlayer → Player RESTRICT` already does today. If a team-delete feature is ever added, it must clean up service-side like §4.4: null `Match.SeasonId` first, then delete.

### 2.7 MatchPlayer (changed)

Existing `EloBefore` / `EloAfter` / `EloChange` keep their meaning for the **all-time ELO** (see §3). Added for seasonal matches:

| New field | Type | Constraints | Description |
|-----------|------|-------------|-------------|
| SeasonEloBefore | int | nullable | `null` for off-season matches |
| SeasonEloAfter | int | nullable | |
| SeasonEloChange | int | nullable | |

These fields are cleared whenever the match leaves its season (season delete §4.4, `EndsAt` shrink §4.3).

---

## 3. ELO Model — Two Ladders

**Decision**: ELO is seasonal; champions and leaderboards are based on seasonal ELO.

Proposed model — every player has:

1. **All-time ELO** (`Player.Elo`, exists today) — updated by **every** match, seasonal or not, in chronological order. This is the answer to *"non-seasonal matches → season 1 → non-seasonal matches: from which part is ELO calculated?"* — the all-time ladder simply counts everything; it never resets and is unaffected by season boundaries.
2. **Seasonal ELO** (`SeasonPlayer.Elo`) — starts at 1000 for everyone, updated **only by that season's matches**. Off-season matches touch it in no way. This ladder decides season standings, champion, and awards.

Consequences of the scoping rule (a match affects exactly: all-time ELO + its own season's ELO, if any):

| Context | ELO used / affected | Stats & badges computed from |
|---------|--------------------|------------------------------|
| Seasonal match | all-time **and** season ladder | — |
| Off-season match | all-time only | — |
| Rankings (season scope, default) | `SeasonPlayer.Elo` | that season's matches only |
| Rankings (all-time scope) | `Player.Elo` | all matches |
| Season champion / awards | seasonal ELO at close | that season's matches only |
| Stats/badges page (default = current season) | seasonal ELO fields | current season's matches |
| Stats/badges page (all-time) | all-time ELO fields | all matches incl. off-season |

Match ELO math (team average, K-factor, expected score) is unchanged — `EloService` just runs once per ladder. That includes the **rating floor** (`ApplyEloChange` clamps at 100): the seasonal ladder inherits it, and since replay (§4.1, §4.3) uses the same function, recomputation stays deterministic. Known quirk: one seasonal match produces **two different ELO changes** — each ladder computes its own expected score from its own ratings; the contextual display rule below decides which one a given screen shows.

**Contextual display rule**: the UI always shows the numbers from the ladder that matters in the given context. A seasonal match (the default) displays seasonal ELO gain, seasonal win rate, seasonal expected-win chance everywhere it appears; an off-season match displays the all-time equivalents. The match detail page leads with the ladder the match belongs to (the other ladder's change is secondary or hidden).

**Ladder-aware `StatContext`** (implementation of the rule above for badges): several stats read ELO values directly — `TopRated`/`LastPlace` use current `Player.Elo`, `PeakElo`/`FurthestFromPeak` use `MatchPlayer.EloAfter`, `TopGainer`/`TopLoser`/`BiggestEloWin`/`BiggestEloLoss` use `EloChange`, `GiantSlayer`/`ChokeArtist`/`Carried` use `EloBefore`. Feeding a season-filtered match list into an unchanged engine would produce season badges with **all-time** numbers. Therefore `StatContext` gains a ladder mode:

- ELO accessors — `EloBeforeOf(mp)` / `EloAfterOf(mp)` / `EloChangeOf(mp)` — return the `SeasonElo*` fields in season scope and the classic fields otherwise; individual stats switch to these accessors instead of touching the properties directly.
- A `CurrentEloOf(player)` accessor returns `SeasonPlayer.Elo` (default 1000 when no row) in season scope, `Player.Elo` otherwise.
- In season scope `StatContext` carries the season's **participant map** (`PlayerId → SeasonPlayer`), loaded by a new season-scoped `StatsEngine` entry point beside `GetAllTimeAsync`. The player pool (`ActivePlayers`) is then **season participants only** — active players *with a `SeasonPlayer` row*. A roster player with no seasonal match is not eligible for pool-based badges (`TopRated`, `LastPlace`, …) and never defaults into the ladder at 1000; the 1000 default matters only for matchmaking (§5.1).
- `StatContext` carries **two orthogonal flags** instead of one: **`Ladder`** (`AllTime` | `Season`) — which fields the ELO accessors read — and **`IsFullScope`** (`true` when the whole selected scope is shown, `false` when a `TimePeriodFilter` narrows it further). `IsAllTime` remains as `Ladder == AllTime && IsFullScope`.
- **`Applies` gating must be updated explicitly** — without this the accessors are dead code, because `IsAllTime == false` alone switches these stats off entirely in season scope:
  - `TopRated`, `LastPlace`, `FurthestFromPeak`: gate changes from `IsAllTime` to `IsFullScope` — "current ELO of the selected ladder" is well-defined for a full season too; they read it via `CurrentEloOf`.
  - `NewcomerStat`: keeps `IsAllTime` (join recency is ladder-independent; stays all-time-only as before).
  - `TopGainer` / `TopLoser`: keep `!IsAllTime` — they already run for period filters and now also run in season scope, reading `EloChangeOf`.

**Win/loss detection**: today wins are detected as `EloChange > 0`. With K=32 and an ELO gap ≳720, the winner's change rounds to 0 and they would be counted as a loser — a latent bug that must not leak into permanent frozen results. All **seasonal** standings, freeze, and award computations determine the winner **by score** (`Team1Score > Team2Score` + the player's `TeamNumber`), not by ELO-change sign. Additionally, **shared code that season scope reuses switches to score as well** — it cannot stay ELO-based there anyway (`SeasonEloChange` is nullable and has the same rounding edge):
- `StatHelpers.IsWinner` — used by eight badge stats directly and by `StreakComputer` (which feeds the four streak stats), in *both* scopes — becomes score-based (`TeamScore(mp.TeamNumber) > OpponentScore(...)`). Behavior is identical except in the rounding edge case, where it is now correct.
- The pair logic reused for pair awards (§8) counts wins by score.

The **all-time variants** of `GetRankingsAsync` and the per-player `StatsService` aggregates keep their current `EloChange > 0` behavior — fixing them the same way is recommended but out of scope. Their **season-scoped counterparts** (§6.2, §6.5) are new or parameterized code and count wins by score from the start. Note the ladder rule also applies to **page-level code outside the stats engine** — the pages that read `Elo*` fields directly are enumerated per page in §6.

**New players joining mid-season**: a newly invited/created player starts with the default ELO (1000) on **both** ladders. No special handling needed — the `SeasonPlayer` row is created lazily with `Elo = 1000` on their first seasonal match, same as everyone else's at season start.

*(Alternative considered: seasonal-only ELO with no all-time ladder — rejected because off-season matches would then affect no rating at all and the all-time rankings page would lose its metric.)*

---

## 4. Season Lifecycle

All lifecycle operations are **admin-only** (`Team.AdminUserId`), with one exception: the **lazy close** (§4.2) is a system action triggered by any member's page load.

### 4.1 Create / start a season

Admin provides: **Name** (required), **Description** (optional), **StartsAt** (default: now, may be in the past or future), **EndsAt** (optional).

- Validation: **unique name** and **no overlap** with existing seasons (§2.1) — the only rules. Consequences of the overlap rule: while the current season is open-ended, any *later* season is blocked (hint: *"End the current season (or set its end date) first."*); a season entirely in the past can always be added for backfill.
- **Concurrency backstop for the creation checks**: the §4.2 locking scheme serializes writes through an *existing* Season row — creation has no row to lock, so the name and overlap validations are check-then-insert and two concurrent creations (double-click, two tabs) could both pass them. Season **create** and **`EndsAt` edit** therefore take a per-team application lock (`sp_getapplock`, keyed by team id) inside their transaction and re-validate (name uniqueness and overlap for create, overlap for the edit) under it — the same serialization idea as §4.2, for the writes that reshape the season timeline itself.
- A season with `StartsAt` in the future is simply **scheduled** — nothing changes until it starts; matches played before `StartsAt` are off-season (or belong to the still-active previous season).

**Importing existing off-season matches**: during creation, the admin sees all **unassigned matches whose `PlayedAt` falls within the new season's period** and can select which to pull in (with a "select all" shortcut). Validation: only unassigned matches of the **same team**, only within `[StartsAt, EndsAt)`. Import exists **only in the creation flow** — a match recorded off-season while a season is active can never join that season later; the escape hatch for mistakes is deleting the season (§4.4) and recreating it with import.

- Imported matches are **replayed in chronological order** (`PlayedAt`, ties by `Id`) to build the seasonal ladder: `SeasonPlayer` rows created, seasonal ELO computed, `MatchPlayer.SeasonElo*` fields filled. All-time ELO is untouched (already applied when the matches were recorded). Cheap at PoC scale.
- This generalization also covers the original "previous season" use case: create a season entirely in the past, import the old matches, and since its `EndsAt` has already passed it can be closed immediately — results and awards generated on the spot.
- **Import races** (accepted at PoC scale): the import list is a snapshot — a match recorded between opening the form and submitting won't appear in it and stays off-season (escape hatch as above). A match deleted (§5.2) concurrently with the creation transaction makes the import replay fail on commit (EF concurrency exception) and roll back harmlessly — the admin simply retries.

### 4.2 End of a season

- **Reaching `EndsAt`**: matches played after `EndsAt` are simply **off-season** (or belong to a newer season) — this is the primary semantic of the end date. Result freezing is lazy: there are no background jobs, so the first page load after `EndsAt` triggers `SeasonService` to close the season (set `ClosedAt`, freeze standings, generate awards). This runs regardless of which member loads the page — it is not an admin action.
- **Manually / prematurely**: admin clicks "End season" → `EndsAt = now` (if unset or in the future), `ClosedAt = now`, standings frozen, awards generated.
- **Idempotency / concurrency**: multiple Blazor circuits can hit the lazy-close moment simultaneously. The close procedure (§8) runs in a transaction that **re-reads the season and re-checks `ClosedAt == null` under an update lock** before doing anything; a concurrent loser sees `ClosedAt` set and does nothing. The `SeasonPlayerResult` PK (§2.3 — result rows exist iff closed, so a duplicate close fails structurally on insert) and the unique index on `SeasonAward (SeasonId, Category, Rank, PlayerId)` are the backstops.
- **Serialization against match writes**: idempotency alone only guards close-vs-close. Recording or deleting a match can race the close itself — a seasonal match committing after the close read its matches but before the close commits would carry the `SeasonId` of a closed season while missing from the frozen standings (the premature "End season" makes this reachable, not just the `EndsAt` boundary). Therefore **every write touching a season's matches takes the same update lock on the Season row** inside its transaction: match creation re-resolves the active season under the lock (§5.1), match deletion of a seasonal match re-verifies the season is open under the lock (§5.2), the close procedure holds the lock for its whole transaction, and the `EndsAt` shrink (§4.3) and season delete (§4.4) hold it for their whole cleanup/replay transactions. All season-touching writes thus serialize through the season row — cheap at PoC scale.
- **Hook placement**: the lazy-close check lives in `TeamAccessService.GetCurrentTeamAsync` (the layout and every team page call it), guarded by a cheap indexed query (`ClosedAt == null && EndsAt <= now`). The query returns **all** pending seasons and the hook closes each — more than one can be pending at once (e.g. a backfilled past season created via §4.1 alongside a naturally ended one). **The check must run before the service's per-circuit cache fast-path**: `GetCurrentTeamAsync` caches the resolved team per circuit and early-returns, so a check placed after the cache would fire at most once per (potentially hours-long) Blazor circuit and "self-heal on next navigation" would not hold within a circuit. Running the guard query on every call is fine at PoC scale. Because page components load data in their own lifecycle methods, the very first render after `EndsAt` may briefly show not-yet-frozen data; it self-heals on the next page load — accepted for the PoC (no correctness impact: an ended-pending-close season is not *active*, so no match can join it in the meantime).
- **Lock implementation note**: EF Core has no pessimistic-locking API — take the season update lock via raw SQL inside the ambient transaction (e.g. `SELECT ... FROM Seasons WITH (UPDLOCK, ROWLOCK) WHERE Id = @id` through `FromSql`), or `sp_getapplock`. For match creation this implies restructuring `MatchService.CreateAsync`: today it reads players and computes ELO *before* opening its transaction; season resolution, `SeasonPlayer` reads/creates, and the seasonal ELO computation must all happen inside the transaction after the lock. (Side benefit: this serializes seasonal match creation per team, closing the existing read-outside-transaction ELO race for seasonal matches.) One future-proofing note: the app currently configures no retry strategy (`EnableRetryOnFailure`); if one is ever added, every explicit transaction introduced here must be wrapped in the execution strategy (`CreateExecutionStrategy().ExecuteAsync`).

### 4.3 Edit

- Rename + edit **Description** anytime (including closed seasons); the new name must stay unique within the team (§2.1, excluding the season itself).
- Edit **EndsAt** of a non-closed season (overlap validation applies). **Every `EndsAt` edit — shrink *or* extend — runs in a transaction that first takes the season update lock (§4.2) and re-checks `ClosedAt == null` under it**; if the lazy close won the race, the edit is rejected with a "season already closed" message. Extending the `EndsAt` of an *ended, pending close* season **revives** it (it becomes active again); matches recorded during the gap stay off-season — import exists only in the creation flow (§4.1) — and the edit form notes this.
  - Moving `EndsAt` **earlier than already-assigned seasonal matches** unassigns those tail matches after an explicit confirmation. Unassignment must keep the ladder consistent: the affected matches' `SeasonId` and `SeasonElo*` fields are cleared **and the seasonal ELO of affected players is rolled back** — either by reverse-applying `SeasonEloBefore` in reverse chronological order, or simply by replaying the whole season ladder (§4.1 machinery); replay is the simpler, always-correct choice at PoC scale. A `SeasonPlayer` left with zero season matches is deleted. The whole shrink (unassign + replay) runs in one transaction holding the season update lock (§4.2) — it mutates the season's matches and must not race a concurrent seasonal match creation or the lazy close.

### 4.4 Delete

Kept — it is cheap to implement and a valuable escape hatch for mistakes (wrong start date, accidental season). Admin-only, with confirmation listing consequences. Because `Match.SeasonId` is `NO ACTION` (§2.6), `SeasonService.Delete` performs the whole cleanup in **one transaction**, holding the season update lock (§4.2) so it cannot race a concurrent seasonal match write or the lazy close:

- `Match.SeasonId` → `null` and `MatchPlayer.SeasonElo*` → `null` for all the season's matches (matches become off-season),
- `SeasonPlayer` (taking its `SeasonPlayerResult` rows along by cascade), `SeasonPair`, and `SeasonAward` rows deleted (**players lose the achievements from that season** — the confirmation must say this explicitly),
- delete the `Season` row,
- all-time ELO unaffected.

---

## 5. Match Recording & Deletion

### 5.1 Recording

- Matches are always recorded with **`PlayedAt = now`** — there is **no backdating** and none is added by this feature. Historical matches enter a season only via the import flow (§4.1). A structural consequence: since closed seasons always lie in the past, a newly recorded match **can never land in a closed season** — frozen results are immutable by construction, no extra validation needed.
- When an active season exists, the new-match form shows a **"Seasonal match" toggle, checked by default**. Unchecking records the match as off-season.
- The toggle is hidden (forced off-season) when no season is active — e.g. after `EndsAt` has passed and before the next season starts, or while the only existing season is merely scheduled. A small hint notes the match will be off-season.
- Assignment rule: `Match.SeasonId` = active season **iff** the toggle is on; otherwise `null`. The active season is resolved **inside the create transaction, under the season update lock** (§4.2) — not from the form's stale state — and with the corrected definition of *active* (§2.1), `PlayedAt = now` then provably lies within `[StartsAt, EndsAt)`. If the season ended (or was closed) between form load and submit, the match is recorded **off-season** with a notice to the user.
- The post-save confirmation shows the ELO change of the match's **own ladder** (seasonal by default, per the contextual display rule §3) and determines the winner **by score** — the current code picks the `MatchPlayer` with `EloChange > 0`, which throws in the rounding-to-zero edge case and would show the wrong ladder's number for seasonal matches.
- **Matchmaking uses the match's ladder**: player picking, team balancing, and the expected-win-chance display in the new-match form are computed from **seasonal ELO** when the match will be seasonal (toggle on, the default), and from **all-time ELO** for off-season matches. Toggling the checkbox re-evaluates them. Players without a `SeasonPlayer` row count as seasonal ELO 1000.

### 5.2 Deletion (existing feature, now season-aware)

The app already allows deleting a match within 24 hours of creation, provided no participant has played a later match; the all-time ELO is reversed by restoring each `MatchPlayer.EloBefore`. Seasons add two rules:

- **Matches of a closed season cannot be deleted.** This is reachable: record a match, admin ends the season prematurely an hour later — the 24h window is still open, but deleting would corrupt frozen standings and awards. `CanDeleteWithReasonAsync` returns a "season is closed" rejection. For a seasonal match, the delete transaction re-verifies the season is still open **under the season update lock** (§4.2), so the check cannot race a concurrent close.
- **Seasonal ELO is reversed too**: for a seasonal match, each participant's `SeasonPlayer.Elo` is restored from `MatchPlayer.SeasonEloBefore`, in the same transaction as the all-time reversal. The existing "no participant has a later match" guard is a superset of the seasonal requirement (any later seasonal match is also a later match), so no additional ordering check is needed. A `SeasonPlayer` left with zero season matches is deleted.

---

## 6. Effect on Existing Pages

General principle: **"current season" is the default lens everywhere** when an active season exists; with no seasons, every page behaves exactly as today. Off-season matches (`SeasonId = null`) appear only in all-time / explicitly unscoped views. Deactivated players are excluded from season-scoped standings and stats (§2.2, §8).

The public landing page (`LandingStatsService`) is unaffected — it reads no ELO and its cross-team counts are season-agnostic.

**Idle default**: when seasons exist but none is active (between seasons, or only scheduled ones), every season-scoped default falls back to **all-time** — the same fallback the Dashboard uses (§6.1). Past seasons remain selectable via the selectors.

### 6.1 Dashboard (`/{TeamCode}`)
- **Season header**: current season name, description, period ("open-ended" if no end date), progress (e.g. "Day 23 · 148 matches").
- Rankings preview (seasonal ELO), recent matches, and stats summary scoped to the current season. The KPI tiles' backing queries (`GetMatchesThisWeekAsync`, `GetAverageMatchScoreAsync`, total-match count) are team-wide today and gain a season parameter like the rankings queries (§6.2).
- Latest season closed and nothing new started: "Season X has ended 🏆" banner linking to its results; content falls back to all-time. If scheduled seasons exist, show the next one's start date ("Season Y starts in 5 days").
- No seasons at all: unchanged from today (+ admin hint "Start your first season").

### 6.2 Rankings (`/{TeamCode}/rankings`)
- Default scope = **current season**, ranked by **seasonal ELO**; W/L, win rate, streaks from season matches only (streak columns are a small new addition — today's rankings model has none). Wins determined by score (§3).
- **Season selector**: current / past seasons / all-time. Past seasons render **entirely from the frozen tables** — `SeasonPlayerResult` (standings incl. win rate, streaks, and position figures; final ELO from the ladder row `SeasonPlayer.Elo`) and `SeasonPair` — zero aggregation. All-time uses `Player.Elo` as today.
- The page's **other sections follow the selector too**: Best Goalkeepers / Best Attackers (goals per game — the same metric the position awards use, §2.5) and Best Pairs (wins by score, via the parameterized pair logic from §8). For the **active** season these aggregate on the fly from season matches; for **closed** seasons they render from the frozen `SeasonPlayerResult` position columns and `SeasonPair` rows. Badge chips next to ranked players come from the season-scoped `StatContext` (§3), not the all-time one; for **closed** seasons the badge chips (and the Stats page's badges, §6.3) remain computed on the fly by the `StatsEngine` — safe because a closed season's matches are immutable (§5.2), and freezing every badge would be overkill. The frozen tables cover the **main figures**: standings, win rate, streaks, position metrics, pairs.
- Implementation note: `GetRankingsAsync`, `GetPositionRankingsAsync`, and `GetPairRankingsAsync` are team-wide all-time queries today, with no match-subset parameter — season scope means parameterizing them (or adding season variants).

### 6.3 Stats (`/{TeamCode}/stats`)
- **Season selector** (current season default / past seasons / all-time). Existing `TimePeriodFilter` remains usable *within* the selected scope.
- Computed badges are therefore **for the current season by default**. Implementation: season resolves to a match subset → `StatsEngine.Compute(filteredMatches)` with the **ladder-aware `StatContext`** (§3) so ELO-based badges show seasonal numbers, not all-time ones.
- The badges are only part of the page — its **inline computations must follow the ladder too** (the `StatContext` accessors don't reach them): the win-rate rankings and pairs chart count wins by **score** (today `EloChange > 0`); the ELO trajectory chart plots `SeasonEloAfter` in season scope (today `EloAfter`); the ELO distribution chart plots `SeasonPlayer.Elo` (today it reuses the all-time rankings' `Player.Elo`). The head-to-head and teammate matrices already judge wins by score — no change.
- Season awards (trophies) are deliberately **not** shown here in v1 — together with the badges it would be too much information. They live on player detail (§6.5) and season detail (§7.2); a dedicated "Hall of Fame" spot can be added later.

### 6.4 Match History (`/{TeamCode}/matches`)
- New filter preset **"This season"** (default when an active season exists) + past seasons selectable.
- Match tiles: **season badge** (name chip); off-season matches get a muted "off-season" chip. The ELO-change badges on tiles (`MatchCard` / `MatchTeamPanel`) show the match's **own ladder** change (§3 contextual rule): `SeasonEloChange` for seasonal matches, `EloChange` for off-season.
- The focused-player summary bar (wins / losses / win rate / net ELO) and the wins/losses outcome filter judge wins by `EloChange > 0` today — both switch to **score**. The net-ELO chip sums `SeasonEloChange` when a season filter is active, `EloChange` otherwise (mixed/unscoped views: every match has an all-time change; only seasonal ones have a seasonal one).
- Match detail: shows season + seasonal ELO changes; its pre-match prediction / win-probability uses `SeasonEloBefore` for seasonal matches (today `EloBefore`). The prev/next navigation (`GetAdjacentMatchIdsAsync`) deliberately stays **unscoped** — it traverses all team matches chronologically regardless of any season filter (it is match-detail chrome, not a scoped list).

### 6.5 Player Detail (`/{TeamCode}/players/{id}`)
- Same season selector (all-time / current / past seasons); default = current season (seasonal ELO graph from `MatchPlayer.SeasonElo*`), all-time available.
- The page also reads the **all-time rankings for the player's rank chip** (`GetRankingsAsync`) — this follows the selector too: live seasonal rank for the active season, frozen `FinalRank` for closed seasons, all-time rank in all-time scope.
- **This is the largest single refactor in the feature** — do not underestimate it. The page is fed by `StatsService.GetPlayerStatsAsync`, a ~330-line all-time aggregate (KPIs, streaks, partners/enemies, expected wins, opponent-strength buckets, clean sheets, milestones, activity) that reads `Player.Elo` / `EloBefore` / `EloAfter` / `EloChange` directly and detects wins via `EloChange > 0` throughout. Season scope parameterizes it by **(match subset, ladder)**: ELO-based figures read the `SeasonElo*` fields, `CurrentElo` comes from `SeasonPlayer.Elo` (default 1000 with no row), wins are determined by **score**. Everything else (positions, partners, margins, day-of-week, monthly activity, milestones) falls out of the subset naturally.
- **Trophy case**: the player's `SeasonAward`s always displayed (gold/silver/bronze icons + category + season name), visually separate from computed badges. Not scoped by the selector — trophies are permanent.

### 6.6 Players (`/{TeamCode}/players`)
- The roster follows the default lens too. With an active season: the rank chips and default ELO sort use `SeasonPlayer.Elo`, the displayed rating is the seasonal one, the last-match trend shows `SeasonEloChange` of the player's last *seasonal* match, and the per-player W/L / win-rate / match-count summaries count season matches only (the page already judges wins by score — no change there). Badge chips switch from today's `StatsEngine.Compute(..., isAllTime: true)` to the season-scoped `StatContext` (§3).
- Players **without a `SeasonPlayer` row** (no seasonal match yet) show a muted "—" instead of a rating and sort after rated players; they are not ranked.
- No season selector here (it's a roster, not a stats page); with no active season the page falls back to all-time per the idle default above. Admin management (add/invite/deactivate/reactivate) is unaffected by seasons.

---

## 7. New "Seasons" Section

New nav item **Seasons** → `/{TeamCode}/seasons` (visible to all members; management controls admin-only).

### 7.1 Season list
- All seasons, newest first; active season highlighted with a "live" indicator; scheduled seasons marked with their start dates.
- Per season: name, period, match count, champion (the Player-gold holder from `SeasonAward` for closed seasons; for a season that generated no awards (§2.5) — or where no player reached the 10-match Player minimum — the `FinalRank = 1` player from the frozen standings).
- Admin controls: **Start new season** (with match import step, §4.1; while the current season is open-ended, a hint explains that a *later* season requires ending it first — creating a past season for backfill remains possible), **End current season**, edit, delete.

### 7.2 Season detail (`/{TeamCode}/seasons/{id}`)
- **Closed**: frozen standings (`SeasonPlayerResult` rows with `FinalRank`, incl. win rate and streaks; final ELO from `SeasonPlayer.Elo`), position tables and pair standings from the frozen data (`SeasonPlayerResult` position columns, `SeasonPair`), awards podium (`SeasonAward`), totals — zero aggregation queries.
- **Active**: live standings computed on the fly (active players only), provisional-awards preview, participants list, admin "End season" action.

---

## 8. Season Close Procedure

Executed by `SeasonService` when a season ends (manually or lazily after `EndsAt`), in **one transaction** that first re-checks `ClosedAt == null` under an update lock (§4.2 idempotency):

1. Freeze results: **insert** one `SeasonPlayerResult` row per participant (§2.3) — `Wins / Losses / MatchesPlayed` (wins by **score**, §3), `LongestWinStreak / LongestLossStreak` (score-based `StreakComputer` over the season's matches in chronological order), and the position figures (`GoalkeeperMatches / GoalsConcededAsGoalkeeper / AttackerMatches / GoalsScoredAsAttacker`); write the `SeasonPair` rows (§2.4, one per duo, wins by score). `FinalRank` (ordered by seasonal ELO, tie-breaks below) is assigned **only to players active at close** — inactive participants get `FinalRank = null`. Frozen views hide participants with `FinalRank == null` and pairs where **either** member has `FinalRank == null` — so what a closed season displays never depends on a player's *current* `IsActive`, only on the state frozen at close. The `SeasonPlayer` ladder rows themselves are untouched — their `Elo` is already final (§2.2).
2. Generate `SeasonAward` rows **only if the season has ≥ 10 matches in total** (§2.5); otherwise skip this step — standings still freeze. Top 3 in each category, eligibility rules per §2.5 — inactive players and pairs with an inactive member excluded. The **pair** logic generalizes the existing `GetPairRankingsAsync` (already ranked by win rate with a 3-game minimum, `MinGamesForPartnerStats` — parameterize the match set, switch wins to score; awards use the **same 3-match minimum** as the rankings). The **position** logic likewise generalizes the existing `GetPositionRankingsAsync` (already ranked by goals per game — parameterize the match set; awards use the **same 5-match minimum**, `MinGamesForPositionBadge`, as the rankings tables). Since the awards use the same metric *and thresholds* as the rankings tables, no new ranking code is needed and the provisional-awards preview (§7.2) is exactly the top of the Rankings page — except the Player category, whose ≥ 10-matches-played minimum (§2.5) filters the standings top before taking three. Inside the close transaction, step 2 may simply read the aggregates step 1 just froze (`SeasonPlayerResult` position columns, `SeasonPair`) instead of re-aggregating — the awards and the frozen tables then agree by construction.
3. Set `ClosedAt` (and `EndsAt` if unset).

**Deterministic tie-breaks** — frozen results must not depend on incidental ordering; ties at ~10-match sample sizes are likely, not theoretical:
- `FinalRank` and live standings: seasonal ELO desc → wins desc → matches played desc → `PlayerId` asc.
- Position awards (goalkeeper, attacker): goals per game (conceded **asc** for goalkeepers / scored **desc** for attackers) → matches in that position desc (larger sample at the same average ranks higher — matches the existing position-rankings convention) → seasonal ELO desc → `PlayerId` asc.
- Pair awards: win rate desc → matches together desc (existing pair-rankings convention) → combined seasonal ELO desc → smaller member `PlayerId` asc.

No JSON blobs — everything strongly typed and queryable. Player names/avatars stay on `Player` (FK, RESTRICT on delete), so results survive renames automatically.

---

## 9. Permissions Summary

| Action | Who |
|--------|-----|
| View seasons, standings, awards | Any team member |
| Create/start season (incl. importing off-season matches) | Admin |
| End season (prematurely) | Admin |
| Lazy close after `EndsAt` (§4.2) | System — triggered by any member's page load |
| Edit season name/description/end date | Admin |
| Delete season | Admin |
| Choose seasonal/off-season on a new match | Anyone recording a match |
| Delete a match (existing rules + §5.2 season rules) | Admin or participant, as today |

Teams may temporarily have **no admin** (`Team.AdminUserId` is nullable; the existing claim-admin banner assigns one). Until claimed, season management is simply unavailable — no special handling; the lazy close is a system action and unaffected.
