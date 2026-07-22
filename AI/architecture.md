# Fotbalek — Target Architecture & Migration Plan

Decisions locked 2026-07-16. This document is the source of truth for the restructure;
iterate here before/while implementing.

## 1. Decisions (locked)

| Topic | Decision |
|---|---|
| Topology | **Single host.** One ASP.NET Core process (`Fotbalek.Web`) hosts Blazor UI, minimal-API endpoints and SignalR. No separate API process, no HTTP hop. Web is a *thin host*: auth, rate limiting, caching, endpoint/response mapping — zero business logic. |
| App-layer entry | Web dispatches **MediatR** commands/queries; handlers return **Result / Result\<T\>**, Web maps them to UI state or HTTP responses. |
| Mediator | **MediatR 12.x** (last Apache-2.0 line, pinned 12.5.x). Wrapped behind our own `ICommand`/`IQuery` so a future swap touches only abstractions. |
| Results | **Custom `Result`/`Result<T>`/`Error` in SharedKernel** — no third-party result library. |
| Identity in Domain | `AppUser : IdentityUser<int>` **stays a Domain entity**. Domain gets exactly one documented package exception: `Microsoft.Extensions.Identity.Stores`. Navigations to `AppUser` keep working. |
| Migration style | **Big-bang** — one restructure landing the full target; ordered internally so it stays mechanical (see §7). |
| Tests | **None now**, but everything is written to be testable later (see §6). |
| Mapping | **Manual mapping** (static/extension mapper methods per feature). No AutoMapper (also commercialized; explicit wins anyway). |
| Validation | **FluentValidation** (Apache-2.0) in Application, executed by a pipeline behavior. |
| Packages | **Central Package Management**: versions in `Directory.Packages.props`, shared build properties (TFM `net10.0`, nullable, implicit usings) in `Directory.Build.props`. |
| Orchestration | **None** (decided 2026-07-16: Aspire dropped from the plan) — one app against the already-existing SQL DBs; run `Fotbalek.Web` directly. Telemetry keeps today's flag-gated Azure Monitor OTel registration in Web. Revisit Aspire only if the topology grows (§8). |
| Scale-out | **Single server instance remains an accepted constraint** (chat pub/sub, presence, game rooms are in-memory). Documented, not "fixed". |

## 2. Solution layout & dependency rules

```
src/
  Fotbalek.sln
  Directory.Build.props          # shared props: net10.0, nullable, implicit usings, warnings
  Directory.Packages.props       # ALL package versions (CPM)
  Fotbalek.SharedKernel/         # Result/Error, Constants, shared enums, primitives
  Fotbalek.Domain/               # entities, domain services (pure logic)
  Fotbalek.Contracts/            # DTOs crossing the Application→Web boundary
  Fotbalek.Application/          # commands/queries/handlers, validators, behaviors, abstractions
  Fotbalek.Infrastructure/       # EF Core (AppDbContext, configurations, migrations), Identity stores
  Fotbalek.Game/                 # pure real-time game core (physics, protocol, sim, bot)
  Fotbalek.Web/                  # host: Blazor components, endpoints, hubs, auth wiring, composition root
```

Allowed references (anything not listed is forbidden):

```
SharedKernel    → (nothing)
Domain          → SharedKernel                  [+ pkg: Microsoft.Extensions.Identity.Stores — the one exception]
Contracts       → SharedKernel
Game            → (nothing)                     [self-contained; no ASP.NET/EF deps]
Application     → Domain, Contracts, SharedKernel
                  [pkgs: MediatR 12.x, FluentValidation (+ its DependencyInjectionExtensions for validator scanning),
                   Microsoft.EntityFrameworkCore.Relational (IAppDbContext/LINQ;
                   Relational rather than core because handlers keep today's ExecuteUpdate/ExecuteDelete bulk ops —
                   those extensions live in the Relational package),
                   Microsoft.Extensions.Identity.Core (UserManager<AppUser> in Features/Admin — password
                   reset/lockout are UserManager operations; host-agnostic pkg, second documented exception)]
Infrastructure  → Application, Domain, SharedKernel
                  [pkgs: EFCore.SqlServer, AspNetCore.Identity.EntityFrameworkCore]
Web             → Application, Infrastructure, Contracts, Game, Domain, SharedKernel
                  [SharedKernel used directly: Result/Error → UI/HTTP mapping (§4.1), Constants]
                  [Infrastructure referenced ONLY for composition-root registration]
                  [Domain referenced ONLY for Identity plumbing — UserManager<AppUser>/SignInManager<AppUser>
                   in auth endpoints + revalidating provider; entities never reach components]
```

Rules of thumb:
- Entities never cross into Web — components render **Contracts DTOs** only. Sole exception:
  `AppUser` inside Web's auth plumbing (`UserManager`/`SignInManager`/revalidating provider);
  it stays out of components and DTO mapping.
- Contracts stay JSON-serializable records (primitives/nested DTOs, never entity references) —
  they are the wire format if the API host split (§8) ever happens.
- Web never touches `AppDbContext` — all data access goes through a dispatched request
  (composition-root exception: the startup-migration call).
- Application never sees `HttpContext`, JS interop, SignalR, or Blazor types.

## 3. What goes where (mapping from current code)

| Current (`Fotbalek.Web/…`) | Target | Notes |
|---|---|---|
| `Constants.cs` | SharedKernel | Elo K-factor, chat constants, time thresholds… |
| `Data/Entities/*` | Domain/Entities | incl. `AppUser` (see decision) |
| `Services/EloService.cs` | Domain/Services `EloCalculator` | pure math — make static |
| `Helpers/SlugGenerator.cs` | **deleted** | was already dead pre-restructure: team code names are user-supplied and regex-validated in `CreateTeamCommand`, nothing generates slugs |
| `Helpers/DateFilterHelper.cs` | **split**: Application/Common + Web | verified 2026-07-16: used only by *components* today (Stats, MatchHistory, MatchDayList) — period filtering happens in-memory in the UI. Range math (`GetDateRange`/`IsDateInPeriod`) → Application/Common: the period + the user's local `today` become query parameters and handlers filter server-side (also what the API split needs). The display-string members (`FormatDayGroup`, `GetPeriodDescription` — English UI text) stay in Web. Already timezone-clean (parameterized by the user's local `today`) |
| `Helpers/PasswordHasher.cs` | Application `ITeamPasswordHasher` + Infrastructure impl | verified 2026-07-16: used only by `TeamService` for **team** passwords — admin login compares config values inline in `AdminEndpoints`. Team create/join move to Application, so hashing must be reachable there: abstraction in `Common/Abstractions` (named `ITeamPasswordHasher` — avoids confusion with Identity's `IPasswordHasher<TUser>`), Infrastructure implements over Identity's `PasswordHasher<T>`. Existing `Team.PasswordHash` values must keep verifying: wrap the same Identity hasher (its user type param is unused by the hash), don't switch algorithms |
| `Data/AppDbContext.cs` | Infrastructure/Persistence | implements `IAppDbContext` from Application |
| `Migrations/*` | Infrastructure/Persistence/Migrations | namespace update only; migration IDs unchanged → existing DBs stay compatible |
| `Services/*Service.cs` (Team, Player, Match, Season, Stats, ShareToken, TeamMembership, Landing, Admin, Chat) | Application/Features/* | rewritten as commands/queries/handlers — this is the bulk of the work (§7 step 6). Admin: the actor check (`EnsureAdminAsync` via `AuthenticationStateProvider`) becomes `IUserContext.IsAdmin`; password reset/lockout inject `UserManager<AppUser>` (pkg exception, §2) |
| `Services/TeamAccessService.cs` | **split**: Application/Common/Authorization + Web | membership/access check → Application (handlers re-verify per dispatch — also what a future API host needs); URL-based current-team resolution (`NavigationManager`, per-circuit cache, lazy `CloseDueSeasons` dispatch) stays in Web — Blazor types can't enter Application |
| `Services/SeasonAggregates.cs` | Domain/Services | pure per-player/per-pair aggregate math over `Match` entities, shared by season close + live standings (was missing from this table) |
| `SeasonService.LockSeasonRowAsync` + `AcquireTeamTimelineLockAsync` | Application `IDbLocks` + Infrastructure impl | (was missing from this table) the pessimistic locks are SQL-Server raw SQL (`UPDLOCK, ROWLOCK` row lock; `sp_getapplock`) on `DatabaseFacade` — provider-specific, can't live in Application. Define `IDbLocks` in Common/Abstractions with both methods; Infrastructure implements them over the scoped `AppDbContext` so the SQL runs on the handler's own connection and joins the ambient transaction. Both locks are Transaction-owned — they *require* an open transaction, which TransactionBehavior guarantees for every command (§4.2). Called from Matches (seasonal create/delete) and Seasons handlers exactly as today |
| `Services/Stats/*` (engine + calculators) | Application/Features/Stats | calculators stay as injected services (`AddFoosballStats` pattern); result records → Contracts |
| `Services/ChatDtos.cs` | Contracts/Chat | |
| `Services/ChatNotifier.cs`, `PresenceTracker.cs`, `PresenceCircuitHandler.cs`, `ChatUiState.cs` | Web/Realtime | circuit-bound UI transport, not business logic |
| `Services/CurrentUserService.cs` | Web implements Application's `IUserContext` | see §4.3 |
| `Services/TimeZoneService.cs` | Web/Services | JS interop = UI boundary (existing timezone policy) |
| `Services/IdentityRevalidatingAuthenticationStateProvider.cs` | Web/Auth | |
| `Configuration/AdminAuth.cs`, `AdminOptions.cs` | Web/Auth | auth is a host concern |
| `Endpoints/AccountEndpoints.cs`, `AdminEndpoints.cs` | Web/Endpoints | keep using SignInManager/UserManager directly (auth = host concern; AdminEndpoints is login/logout only — config-value compare + admin cookie); admin *operations* (user list, password reset — invoked from the admin pages, there is no user-delete today) move to Application/Features/Admin |
| `Game/Core/*` | **Fotbalek.Game** | already pure ("no server dependencies, unit-testable") |
| `Game/GameHub.cs`, `GameRoom.cs`, `GameRoomManager.cs`, `GameTelemetry.cs` | Web/Game | SignalR-coupled transport + in-memory room state. `GameHub.JoinRoom` is the hub's one data touchpoint — its membership check + player lookup (`TeamMembershipService.IsMemberAsync`, `PlayerService.GetUserPlayerInTeamAsync`) become dispatched Application queries (§4.4) |
| `Components/*`, `wwwroot/*` | Web | rewired to `IScopedSender` + DTOs |

Application feature layout (vertical slices; "shared things go to services" → `Common/`):

```
Fotbalek.Application/
  Common/
    Abstractions/      # IAppDbContext, IUnitOfWork, IUserContext, IScopedSender, ICommand/IQuery,
                       # IDbLocks (§3), IEventCollector (§4.2), ITeamPasswordHasher
    Behaviors/         # Logging → Validation → Transaction (commands only)
    Authorization/     # TeamAccess checks (from TeamAccessService)
  Features/
    Teams/  Players/  Matches/  Seasons/  Stats/  Chat/
    Memberships/  ShareTokens/  Admin/  Landing/  Account/
      └─ e.g. Matches/CreateMatch/CreateMatchCommand.cs + Handler + Validator
  DependencyInjection.cs   # AddApplication()
```

## 4. Cross-cutting patterns

### 4.1 Result + Error (SharedKernel)
- `Result` / `Result<T>` with `Error(Code, Message, Type)`; `ErrorType`: `Validation`, `NotFound`, `Conflict`, `Unauthorized`, `Forbidden`, `Failure`.
- Validation errors carry field-level details (for form display).
- Web mapping: Blazor components render errors inline; HTTP endpoints map `ErrorType` → status code (+ ProblemDetails where JSON is returned).
- Exceptions are not flow control: `Result` covers *expected* failures; there is no exception-catching pipeline behavior — unexpected exceptions are bugs and bubble to host error handling (error page / circuit boundary) as today.

### 4.2 Mediator abstractions & pipeline
- `ICommand : IRequest<Result>`, `ICommand<T> : IRequest<Result<T>>`, `IQuery<T> : IRequest<Result<T>>` + matching handler interfaces. Code never references MediatR types outside `Common/Abstractions` + DI setup — **documented exception: the notification path** (`INotification` event records, the `IEventCollector` queue and its post-commit `IPublisher` flush in TransactionBehavior, Web's bridge `INotificationHandler`s, §4.4). Wrapping it buys nothing: a mediator swap would rewrite that path anyway. DI setup must scan the Web assembly too, or the bridge handlers won't register.
- Behaviors (order): **Logging → Validation → Transaction**.
  - ValidationBehavior: runs FluentValidation validators, short-circuits to `Result` failure.
  - TransactionBehavior: **every command** (the `ICommand` marker; queries are never wrapped); wraps the handler via `IUnitOfWork.BeginTransactionAsync`. Wrapping even single-`SaveChanges` commands is deliberate: the cost is negligible at this scale, and it guarantees (a) an open transaction whenever a handler takes an `IDbLocks` lock — both locks are Transaction-owned (§3) — and (b) exactly one commit point per dispatch for the event flush below. Guard: no-op when a transaction is already active on the context — a handler dispatching a sub-command in the same scope must join the outer transaction, not have `BeginTransaction` throw.
  - **Events flush after commit.** Handlers never call `IPublisher` mid-handler: with the behavior's transaction open, a publish after `SaveChanges` still runs *before commit*, so the Web bridge would fan out state that can roll back (ghost chat messages on every subscribed circuit). Instead handlers enqueue `INotification`s on a **scoped `IEventCollector`**; the TransactionBehavior that *owns* the transaction publishes the queue via `IPublisher` immediately after a successful commit — still synchronously, before the dispatch returns, so the sender's own circuit keeps seeing its event as part of the round trip. Joined (nested) dispatches don't flush; the owner flushes the whole scope's queue. A failed handler/commit discards the queue.
  - ⚠ User-initiated transactions are incompatible with connection resiliency: `EnableRetryOnFailure` makes `BeginTransaction` throw at runtime. Today's registration has no retry — keep plain `UseSqlServer` in `AddInfrastructure()`; if resiliency is ever wanted, TransactionBehavior must wrap the handler in `CreateExecutionStrategy().ExecuteAsync(...)`.
- API/endpoint style: head toward RESTful (correct verbs, resource paths) without being dogmatic — applies to the minimal-API endpoints that remain.

### 4.3 Dispatch scoping — the Blazor Server gotcha (important)
Blazor Server scoped services live for the whole circuit (hours). That's why the code
uses `IDbContextFactory` today. Under the new architecture:

- **`IScopedSender`** is the single dispatch entry point for components: it creates a
  fresh DI scope per dispatch, seeds the scope's `IUserContext` from the caller
  (circuit `AuthenticationStateProvider`, `HttpContext.User` in endpoints, or `Context.User`
  in SignalR hubs — GameHub's join path, §4.4), resolves
  `ISender` inside that scope, and sends.
- `AppDbContext` becomes a plain **scoped** registration — one context per dispatch =
  one unit of work. `IDbContextFactory` goes away entirely: its only consumers are the
  services being rewritten, and the two apparent edge cases already resolve the scoped
  context through their own scopes today (startup migration via `CreateScope`,
  revalidating auth provider via `IServiceScopeFactory` → `UserManager`) — verified
  2026-07-16.
- Handlers consume `IUserContext` (user id, admin flag) — never `ClaimsPrincipal`,
  never ambient circuit state. `IUserContext` models the anonymous case (null user id):
  the public landing query ("/") and the pre-login pages dispatch with no principal.
  This also keeps handlers trivially testable.
- Rule: **DB access happens only inside a dispatch scope or an HTTP request scope.**

### 4.4 Realtime bridges (chat, presence, game)
- Chat/read-state/reaction **writes** are Application commands. The handler enqueues an
  `INotification` (e.g. `ChatMessagePosted`) on `IEventCollector`; it is published after
  the command's transaction commits (§4.2). Web registers notification handlers that
  forward to `ChatNotifier`, which keeps fanning out to circuits exactly as today.
  Typing indicators stay Web-only (ephemeral, no DB, no business logic).
  - The in-memory send throttle (`ChatNotifier.TryRecordSend`) stays in Web and runs
    **before dispatch** — rate limiting is a host concern (§1); membership/length checks
    stay in the handler/validator. (Small semantics shift, accepted: today the throttle
    runs *after* the trim/empty and membership checks inside `ChatService.SendAsync`, so
    pre-dispatch it consumes a slot even for a send that later fails validation — fine
    for a soft in-memory limit.) Clearing typing-on-send moves into the Web bridge
    handler for `ChatMessagePosted` (it has team + sender). `ChatService.SendResult`-style
    enums become `Error` codes on `Result`.
- Presence stays a Web singleton + circuit handler (unchanged).
- Game: `Fotbalek.Game` = pure sim (physics/protocol/state/bot). Hub, rooms, room
  manager, telemetry stay in Web — they're SignalR transport around the sim. The
  server/client formula-mirror constraint (rod prediction) is unaffected.
  One exception to "transport only": `GameHub.JoinRoom` verifies team membership and
  resolves the caller's player (display name/avatar) before seating the connection —
  after the rewrite these are Application queries dispatched per §4.3 (each hub
  invocation gets its own DI scope, so scoped resolution works). The input/lifecycle
  hub methods stay DB-free.

### 4.5 Caching & rate limiting (basic, for now)
- Rate limiting: keep the admin-login fixed-window limiter (Web).
- Caching: none exists today. First candidate when wanted: landing-page aggregates
  (`GetLandingStatsQuery`) via `IMemoryCache`/`HybridCache` in Web or a decorating
  behavior. Not part of the big-bang.

### 4.6 Identity & auth (host concern)
- `AddInfrastructure()` registers DbContext + `AddIdentityCore<AppUser>(options).AddRoles<IdentityRole<int>>().AddEntityFrameworkStores<AppDbContext>()`.
- `AddSignInManager()` and `AddDefaultTokenProviders()` cannot move there — both extensions live in
  the ASP.NET shared framework, which a plain class library doesn't reference (verified: doesn't
  compile in Infrastructure). Web layers both on via a fresh `IdentityBuilder` over the same
  service collection; the default token providers are needed by admin password reset.
- Web owns: cookie schemes (Identity + parallel admin cookie), policies, the
  identity-merge middleware, login/register/logout + admin-login endpoints
  (these keep using `SignInManager`/`UserManager` directly — auth is host logic).
- Admin *operations* behind the endpoints move to Application (`Features/Admin`).

## 5. Solution infrastructure

### 5.1 Central build & packages
- `Directory.Build.props`: `TargetFramework=net10.0`, `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors` (decide during implementation).
- `Directory.Packages.props`: every `PackageVersion` centrally; csproj files contain only versionless `PackageReference`s. Pin `MediatR` 12.5.x.

### 5.2 Run, telemetry & deployment (no orchestrator)
- No AppHost/ServiceDefaults (decided 2026-07-16): one app, and the dev/prod SQL DBs
  already exist with data — an orchestrated container DB would solve a problem we don't have.
- Dev run: `Fotbalek.Web` directly (launchSettings / `.claude/launch.json`, both unchanged —
  the Web project path stays `src/Fotbalek.Web`), against the existing local SQL DB via
  `ConnectionStrings:DefaultConnection`.
- Telemetry: keep today's flag-gated Azure Monitor OTel registration, now in Web's
  composition root (codeless-agent-disable caveat still applies). Health checks: none
  exist today; plain `AddHealthChecks()` is a later option, not part of the big-bang.
- Migrations still run at startup (PoC simplicity) — fine for single instance; a
  dedicated migration worker is a later option.
- **Prod deploy unchanged:** publish `Fotbalek.Web` (existing zip-deploy publish profile) —
  project references flow into the publish output automatically; config (connection
  string, telemetry flag) keeps coming from App Service settings.

### 5.3 EF migrations workflow
`dotnet ef migrations add X --project Fotbalek.Infrastructure --startup-project Fotbalek.Web`
Existing migrations move with namespace updates only; `__EFMigrationsHistory` stores
IDs, not assembly names → applied databases remain compatible.

## 6. Testability guidelines (tests come later)
- All logic behind constructor-injected dependencies; no service location, no statics
  with state.
- Pure logic stays pure: `EloCalculator`, `Fotbalek.Game`, stats calculators.
- Handlers depend only on `IAppDbContext`/`IUnitOfWork`/`IUserContext`/domain services.
- New code that needs "now" should take `TimeProvider` (don't retrofit existing
  `DateTimeOffset.UtcNow` calls during the move).
- Future test projects: `tests/Fotbalek.{Domain,Application,Game}.Tests` + an
  architecture-rules test enforcing §2's dependency table.

## 7. Migration plan (big-bang, ordered)

One landing, executed in this order so each step is mechanical and reviewable:

1. **Scaffold** — create all projects + references per §2; `Directory.Build.props`,
   `Directory.Packages.props`; add MediatR/FluentValidation packages. Solution builds
   with empty projects.
2. **SharedKernel** — move `Constants`; write `Result`/`Result<T>`/`Error`; move shared
   enums (any enum used by both entities and DTOs).
3. **Domain** — move `Data/Entities/*`; `EloService` → `EloCalculator`; `SlugGenerator`;
   `SeasonAggregates` → Domain/Services.
4. **Infrastructure** — move `AppDbContext` + `Migrations` (namespace sweep); define
   `IAppDbContext`/`IUnitOfWork`/`ITeamPasswordHasher`/`IDbLocks` in Application and
   implement; `AddInfrastructure()` (DbContext + Identity stores).
5. **Contracts** — move `ChatDtos`, stats result records; create DTOs per feature as
   step 6 consumes them.
6. **Application** *(the bulk)* — abstractions (`ICommand`/`IQuery`, behaviors,
   `IUserContext`, `IScopedSender`, `IEventCollector`), then rewrite services → feature
   slices, one
   feature at a time: Teams, Players, Matches (+Elo flow), Seasons, Stats (move engine),
   Chat (+notifications), Memberships, ShareTokens, Admin, Landing, Account queries.
   `AddApplication()`.
7. **Game** — extract `Game/Core` → `Fotbalek.Game`; hub/rooms/telemetry stay in Web.
8. **Web rewire** — components: inject `IScopedSender`, render DTOs, handle `Result`
   errors inline; endpoints slimmed to auth + dispatch; notification→`ChatNotifier`
   bridge; `Program.cs` becomes composition root
   (`AddApplication().AddInfrastructure(cfg)` + auth/rate-limit/telemetry/Blazor wiring);
   `_Imports.razor` namespace updates.
9. **Cleanup** — delete emptied folders, full namespace sweep
   (`Fotbalek.Web.*` → per-project), rewrite XML-doc crefs that now cross forbidden
   boundaries as plain text (`Constants` → `SeasonAward.Category`, `ChatDtos` →
   `ChatReadState`), verify `.claude/launch.json` still launches Web (project path
   unchanged), update `admin.md`/docs.
10. **Verify** — build with zero warnings-of-interest; run `Fotbalek.Web` against the
    local DB; smoke:
    register/login/logout, landing page (privacy: aggregates only), create/join team,
    record match (Elo applied), stats pages, local-time rendering (timezone policy:
    dates/times must still convert via the browser TZ at the UI boundary), chat between
    two sessions (post/edit/delete/react/read-receipts/typing), presence, live game with
    two tabs (+bot), admin login (rate limit) + admin ops, share-token link.

Highest-risk areas to watch during verification: dispatch-scope auth state (§4.3),
chat notification timing (events must flush post-commit via `IEventCollector` — never
mid-transaction, §4.2), Elo/season aggregate transactionality + the `IDbLocks` season
locks running inside the behavior's transaction, EF migrations after the namespace
move, antiforgery on the auth form posts.

## 8. Deferred / future
- Test projects + architecture tests (§6).
- Real caching (HybridCache) where measurements justify it.
- .NET Aspire (AppHost + ServiceDefaults) — dropped from the plan 2026-07-16: with one
  app and existing DBs it added orchestration ceremony for nothing. Revisit if the
  topology grows — the API/Web split below, a dedicated migration worker, or wanting a
  containerized dev SQL instead of the shared local DB.
- Splitting API and Web into separately scaling hosts — trigger: a non-browser client,
  or API and Web (circuits) needing independent scale. Largely split-ready by design:
  components speak `IScopedSender` + Contracts, so the Web side swaps in an HTTP-backed
  dispatcher while the API host maps the same commands/queries to endpoints;
  `IUserContext` seeds from `HttpContext.User` there; handlers re-verify team access per
  dispatch (not per circuit); Contracts are already the wire format (§2 rule). What the
  split *adds*:
  - Command/query records live in Application (decided 2026-07-16, slice ergonomics win),
    so the Web host keeps referencing Application (+ Domain transitively) for the *request
    types only* — handlers never execute there (Infrastructure isn't registered). Accept
    that deploy coupling, or do the then-mechanical move of the records to Contracts
    (`ICommand`/`IQuery` would move to SharedKernel on the MediatR.Contracts micro-package).
  - The MediatR-notification → `ChatNotifier` bridge is in-process, so API-side writes
    need a cross-process path to Web-side circuits (Redis pub/sub, Azure SignalR, bus).
  - Cookie auth spanning both hosts needs a shared Data Protection key ring (antiforgery
    included) — and the Web-side HTTP dispatcher must propagate the caller's identity:
    a circuit has no live `HttpContext` after connect, so it's the cookie captured at
    circuit start (expiry caveat) or a signed user-context header/token.
  - The in-memory presence/game-room state hits the scale-out bullet above. Which host
    owns GameHub is part of that decision (see the Azure SignalR note below) — and its
    `JoinRoom` membership/player queries (§4.4) need a dispatch path from that host.
- Scale-out (backplane for chat/presence/game) — explicitly out of scope.
- Azure SignalR Service — evaluated 2026-07-16, rejected for now: the 30 Hz game
  snapshots blow through the free tier in minutes and the paid tier adds a proxy
  hop (latency) to the latency-sensitive game for no functional gain; it manages
  connections, not the in-memory room/presence/chat state that actually blocks
  scale-out; the local emulator is serverless-mode-only (GameHub is default mode).
  Revisit if: >1 instance needed, App Service WebSocket limits are hit, or the API
  host split happens. Even then the game hub likely stays self-hosted; circuits and
  chat-style hubs are the candidates (one-liner at the host thanks to §2 layering).
