# Account Page & Admin Interface — Feature Spec

> **Note (2026-07-16):** this spec predates the clean-architecture restructure (see
> `AI/architecture.md`). File paths and service names below refer to the old single-project
> layout — e.g. `Services/AdminService.cs` is now `Fotbalek.Application/Features/Admin`,
> `TeamAccessService` is now `CurrentTeamProvider` (Web) + `TeamAccess` (Application), and
> entities live in `Fotbalek.Domain`. The behavior described here is unchanged.

Three features that work together:

1. **`/account`** — a page for logged-in users: profile overview (teams + their claimed player per team) and change-password.
2. **`/admin`** — a global admin interface authenticated against credentials from configuration (appsettings/user-secrets locally, host app settings — optionally Key Vault references — in production), not against the Identity user store.
3. **Admin user management (v1)** — list all users; reset a user's password to a generated short temp password that the admin hands over manually; the user then logs in and changes it on `/account`.

Preceded by two prerequisite refactors (§0): team routes move under `/team/{codename}` (frees the top-level namespace for `/account`, `/admin`, and anything future), and the team-level "admin" role is renamed to **captain** (so "admin" only ever means the global persona).

---

## 0. Prerequisite refactors (do these first)

### 0a. Team routes move under `/team/{codename}`

Today team pages live at the root (`/{TeamCode}`, `/{TeamCode}/matches`, …), which collides with every literal top-level route. `TeamAccessService.GetTeamCodeFromUrl()` already carries a hand-maintained reserved-word list (`login`, `register`, `profile`, `teams`, `create`, `join`, `account`, `not-found`) — and it is already missing `admin`. Prefixing kills this problem class for good:

- All 13 team-page `@page` directives (Dashboard, Rankings, Stats, MatchHistory, MatchDetail, NewMatch, Players, PlayerDetail, Seasons, SeasonNew, SeasonDetail, Settings, ClaimPlayer) change from `/{TeamCode}/…` to `/team/{TeamCode}/…`.
- `TeamAccessService.GetTeamCodeFromUrl()`: return the second path segment when the first is `team`, else null — and **delete the reserved-word list**.
- All link/navigation construction — ~78 sites across ~19 files (`TeamLayout`, `Home`, `Create`, `Join`, match/team pages, and the shared `MatchCard`, `PlayerCard`, `MatchTeamPanel`, `PositionRankingTable` components). Mechanical, but don't grep for individual href shapes — sweep `teamcode|codename` **case-insensitively** across `Components/` and review every hit (lowercase locals like `teamCode`/`codeName` carry several of the URLs; a case-sensitive sweep misses those lines). The variants in play: `/@TeamCode/…` (the most common), `/@_team.CodeName/…`, `@($"/{_team.CodeName}/…")`, `$"/{TeamCode}/…"` and `$"/{_team.CodeName}/…"` in `NavigateTo` calls, plus four easy-to-miss sites: `TeamLayout` builds the login `returnUrl` as `$"/{teamCode}"`, `TeamLayout.SwitchTeam(string codeName)` navigates to `$"/{codeName}"`, `TeamLayout.GetTeamUrl()` builds the copy-to-clipboard share URL as `$"{baseUrl}/{_team?.CodeName}"`, and `Settings.razor` displays the literal text `<code>/@_team.CodeName</code>`.
- **Legacy redirect** so old bookmarks and shared links keep working: new `LegacyTeamRedirect.razor` with `@page "/{TeamCode}"` and `@page "/{TeamCode}/{*Rest}"`, redirecting to `/team/{TeamCode}/{Rest}`. Preserve the query string (append `new Uri(Navigation.Uri).Query`, so old `/{code}/matches?season=…` links survive) and navigate with `replace: true` so the redirecting URL doesn't stay in history and trap the back button. Literal routes (`/admin`, `/account`, `/login`, …) take precedence over parameterized ones, so the redirect cannot shadow them.
  - **Update — later removed.** The old root URLs only ever existed in pre-migration local dev (the route move and the redirect landed in the same commit), so this backwards-compat catch-all was dropped. Its bigger cost was that, being a root-level catch-all, it matched *every* unmatched GET — stray probes, mistyped paths, and (in dev) asset requests made before a rebuild refreshed the `MapStaticAssets` fingerprint manifest — and raised a `NavigationException` on each. Unmatched paths now fall through to the router's `NotFoundPage`, which sets a real 404 status during SSR.
- Codename validation needs **no reserved-name list** — under `/team/` nothing competes with team codes.

### 0b. Team "admin" → captain

"Admin" is about to mean the global config-authenticated persona (§2); the team-level role becomes **captain** so the vocabulary stays unambiguous. Full rename:

- `Team.AdminUserId` → `Team.CaptainUserId`, `Team.AdminUser` → `Team.CaptainUser` (the navigation property), `AppUser.AdministeredTeams` → `AppUser.CaptainedTeams` — one trivial rename migration (EF scaffolds the column/FK/index renames).
- Code: `TeamAccessService.IsAdminAsync` → `IsCaptainAsync`, `TeamService.TryClaimAdminAsync` → `TryClaimCaptainAsync`, `_isAdmin` → `_isCaptain`, plus remaining `AdminUserId` references (grep `AdminUserId|IsAdmin|ClaimAdmin` — TeamService, PlayerService, MatchService, SeasonService, TeamLayout, team pages, and `AppDbContext`, whose Team↔AppUser FK configuration uses all three renamed members).
- UI strings: "Admin" badge → "Captain", "Become admin" → "Become captain", "This team has no admin yet" → "…no captain yet", and any settings-page wording (several pages carry "Only the team admin can …" messages). Finish with a case-insensitive `admin` sweep over `Components/` for stragglers the identifier grep can't see (e.g. the header comment in `SeasonManageActions.razor`) — and include `.razor.css` files in the sweep: `Players.razor.css` styles the `admin-action` class used by the captain-only buttons in `Players.razor`, so rename the class on both sides (markup + css) or deliberately keep it; a markup-only rename silently breaks the hover styling.

## 1. Account page (`/account`)

Interactive Blazor page, `@attribute [Authorize]`, default `MainLayout`.

### Content

- **Profile card**: username, member since (`AppUser.CreatedAt`, format via `TimeZoneService` per the project timezone policy). Note: `Tz.EnsureResolvedAsync()` is only called by `TeamLayout` and the team pages — nothing under `MainLayout` calls it, so this page (and the admin users page) must call it itself in `OnAfterRenderAsync` before formatting dates.
- **Teams section**: one card/row per membership (`TeamMembershipService.GetTeamsForUserAsync` extended or a new query):
  - Team name + `@codename` linking to `/team/{codename}`, joined date.
  - The user's claimed player in that team, if any (`Player.UserId == userId && Player.TeamId == teamId`): avatar, player name, ELO, active/inactive badge. If no claimed player, show a muted "No player claimed" with a link to `/team/{codename}/claim`.
  - Badge if the user is that team's captain (`Team.CaptainUserId == userId`, after §0b).
- **Change password card**: classic form POST (same pattern as `Login.razor` — antiforgery token, redirect back with `?error=`/`?success=` query params):
  - Fields: current password, new password, confirm new password.
  - Posts to new endpoint `POST /account/change-password`.

### New endpoint in `AccountEndpoints.cs`

`POST /account/change-password` — map with `.RequireAuthorization()`; under the hardened default policy (§2) that also rejects admin-only sessions, which is correct here:

1. Resolve user from `HttpContext.User` via `UserManager`.
2. Validate new password == confirmation (redirect back with error otherwise).
3. `userManager.ChangePasswordAsync(user, current, new)` — this handles current-password verification and policy (min length 6). On failure redirect back with joined error descriptions.
4. **Important:** `ChangePasswordAsync` rotates the security stamp — call `signInManager.RefreshSignInAsync(user)` afterwards, otherwise the revalidating auth-state provider signs the user out within 30 minutes and the current cookie dies on next validation.
5. Redirect to `/account?success=password-changed`.

### Navigation

- `TeamLayout` user dropdown: add a "My account" item (`/account`).
- `Home.razor` (authenticated state): add an "Account" button next to Create/Join/Sign out.

---

## 2. Admin authentication (config-based, `/admin`)

### Configuration

```json
// appsettings.json (committed — empty defaults, admin disabled)
"Admin": {
  "Username": "",
  "Password": "",
  "ContactEmail": ""
}
```

- Locally: fill in `appsettings.Development.json` or user-secrets.
- Production: environment variables / host app settings `Admin__Username`, `Admin__Password`, and `Admin__ContactEmail` (standard `__` → `:` mapping) — the same channel that supplies `ConnectionStrings__DefaultConnection` today. **Note:** the codebase has no Key Vault configuration provider. If Key Vault should hold the values, either point the two app settings at KV via App Service *Key Vault references* (no code change), or add the `Azure.Extensions.AspNetCore.Configuration.Secrets` provider (then secret names `Admin--Username` / `Admin--Password` map to the section).
- Bind to an options class:

```csharp
public class AdminOptions
{
    public const string SectionName = "Admin";
    public string? Username { get; set; }
    public string? Password { get; set; }

    // Public contact address shown on the login page (forgot-password hint).
    // Independent of the credentials: it can be set while admin login is not, and vice versa.
    public string? ContactEmail { get; set; }

    // Auth only — ContactEmail deliberately not included.
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
}
```

**Not-configured behavior (hard requirement):** the app must start and run normally. `/admin/login` renders a notice ("Admin access is not configured on this instance.") and the login POST always fails with a generic error. No exceptions anywhere.

### Auth design — second cookie scheme

Do **not** put the admin into the Identity user store or the Identity application cookie. Use a parallel cookie scheme:

```csharp
// AddIdentityCookies() returns IdentityCookiesBuilder, not AuthenticationBuilder —
// AddCookie cannot be chained after it; keep the builder in a local.
var authBuilder = builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme);
authBuilder.AddIdentityCookies();
authBuilder.AddCookie(AdminAuth.Scheme, opt =>   // AdminAuth.Scheme = "AdminCookie"
{
    opt.Cookie.Name = "fotbalek.admin";
    opt.LoginPath = "/admin/login";
    opt.AccessDeniedPath = "/admin/login";
    opt.ExpireTimeSpan = TimeSpan.FromHours(8);
    opt.SlidingExpiration = false;
});

// Replaces the existing bare builder.Services.AddAuthorization() call.
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AdminAuth.Policy, p => p.RequireClaim(AdminAuth.ClaimType, "true"))
    // Default-policy hardening: an admin-only principal IS authenticated, so without this
    // every bare [Authorize] page (Create, Join, all team pages, the new /account) would
    // render for an admin who is not logged in as an app user — and then misbehave when
    // CurrentUserService returns null. Identity cookie principals always carry
    // NameIdentifier, so normal users are unaffected; admin-only sessions get treated as
    // unauthenticated on user-facing pages (RedirectToLogin sends them to /login).
    .SetDefaultPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireClaim(ClaimTypes.NameIdentifier)
        .Build());
```

Constants (`AdminAuth` static class): scheme name, policy name (`"AdminOnly"`), claim type (e.g. `"fotbalek:admin"`).

### Blazor circuit caveat — identity merge middleware (the critical piece)

The whole app renders InteractiveServer; `AuthorizeRouteView`/`AuthorizeView` evaluate against the **cascading AuthenticationState**, which is seeded from `HttpContext.User` at circuit start — and `UseAuthentication()` only populates the **default** scheme. The admin cookie would be invisible to Blazor. Fix with a small merge middleware placed **between** `UseAuthentication()` and `UseAuthorization()`:

```csharp
app.Use(async (ctx, next) =>
{
    var admin = await ctx.AuthenticateAsync(AdminAuth.Scheme);
    if (admin.Succeeded && admin.Principal != null)
        ctx.User.AddIdentities(admin.Principal.Identities);
    await next();
});
```

The admin identity must carry **only** a `Name` claim and the admin claim — **no `ClaimTypes.NameIdentifier`**. Existing code (`CurrentUserService`, layouts) parses `NameIdentifier` as the app user id; the admin identity must never masquerade as an app user. An admin can simultaneously be logged in as a normal user — both identities coexist on the principal and both features keep working.

Display-name caveat: with an admin-only session the principal's *primary* identity is the unauthenticated default one, so `User.Identity.Name` is `null`. Anywhere the admin UI shows who is signed in, read the name off the admin identity explicitly (e.g. `User.Identities.FirstOrDefault(i => i.HasClaim(AdminAuth.ClaimType, "true"))?.Name`).

The default-policy hardening above (`RequireClaim(NameIdentifier)`) is the other half of this design: it keeps the admin-only session from passing bare `[Authorize]` on user-facing pages. (`Home.razor` and `TeamLayout` are already safe — they gate on `CurrentUserService`, not on `AuthorizeView` — but `Create`/`Join`/`Account` rely on `[Authorize]` + `.Value` on the user id.)

### Revalidation provider change

`IdentityRevalidatingAuthenticationStateProvider.ValidateAuthenticationStateAsync` returns `false` when `userManager.GetUserAsync` finds no user. In practice the revalidation loop should never even start for an admin-only circuit — `RevalidatingServerAuthenticationStateProvider` only spins it up when the principal's *primary* identity is authenticated, and for an admin-only session the primary identity is the unauthenticated default one (same framework detail as the display-name caveat above; confirm at step 3 of the implementation order). Still change the `user is null` branch, so correctness doesn't silently ride on that detail:

```csharp
if (user is null)
    return authenticationState.User.HasClaim(AdminAuth.ClaimType, "true")
        && !authenticationState.User.HasClaim(c => c.Type == ClaimTypes.NameIdentifier);
```

The `NameIdentifier` guard keeps the dual-login edge correct: if the principal carries a user identity whose account no longer exists (deleted user), the circuit must die — bare `HasClaim(admin)` would keep the stale user identity alive and passing the hardened default policy. When both identities are present and the user exists, the normal security-stamp validation runs — correct.

**Accepted limitation (v1):** cookie lifetime is enforced at the HTTP layer only. A continuously connected circuit is never re-authenticated against the cookie, so an admin tab left open keeps its authorized circuit past the 8-hour expiry (the revalidation loop can't catch this — it doesn't run for admin-only sessions, and it checks security stamps, not cookie expiry). Any full HTTP request — hard navigation, any form POST including logout — re-checks the cookie and bounces. Fine for v1; revisit only if admin sessions ever need hard expiry.

### Login/logout endpoints (`Endpoints/AdminEndpoints.cs`)

- `POST /admin/auth/login` — form fields `username`, `password`:
  - If `!options.IsConfigured` → redirect `/admin/login?error=1` (same generic error as bad credentials — don't leak which case it is).
  - Compare `SHA256.HashData` digests of the UTF-8 bytes of each value using `CryptographicOperations.FixedTimeEquals` (hashing first equalizes lengths — `FixedTimeEquals` returns early on length mismatch — so the comparison is constant-time regardless of input).
  - On success: `SignInAsync(AdminAuth.Scheme, principal)` where principal = `new ClaimsIdentity(claims, AdminAuth.Scheme)` with `Name = username` + the admin claim; redirect `/admin`. The authenticationType constructor argument matters: without it the identity reports `IsAuthenticated == false`. Nothing in v1 happens to check that on the admin identity (the admin policy is claim-based), but any future `RequireAuthenticatedUser`-style requirement on the admin policy would silently fail — don't leave the trap.
- `POST /admin/auth/logout` — `SignOutAsync(AdminAuth.Scheme)`; redirect `/admin/login`.
- **Antiforgery**: minimal-API `[FromForm]` endpoints validate the antiforgery token automatically — both the AdminLogin form and the AdminLayout logout form must render `<AntiforgeryToken />` (same as `Login.razor` / the existing logout forms).
  - Cross-identity caveat (accepted, don't engineer around it): antiforgery tokens are claim-bound. For user and dual sessions the binding comes from the first authenticated identity's `NameIdentifier` — the user identity — so an admin logging in or out does **not** invalidate user-page forms. Admin-only tokens fall back to being bound to the full admin claim set, so a form rendered under an admin-only session and posted after the session changed (user logged in in another tab, or the admin cookie expired) fails with HTTP 400; a page reload fixes it. Admin-facing, transition-window-only.
- **Rate limiting**: add a fixed-window rate limiter (e.g. 5 attempts/min per IP) on the login endpoint — config credentials get no Identity lockout protection, so this is the only brute-force guard. Needs `builder.Services.AddRateLimiter(...)` + `app.UseRateLimiter()` in `Program.cs` and `.RequireRateLimiting(...)` on the endpoint. **Rejection must redirect, not return a bare status code**: in `OnRejected`, `Response.Redirect("/admin/login?error=rate")`, and add the matching message to AdminLogin ("Too many attempts — try again in a minute."). Otherwise the bodiless 429 gets re-executed by the existing `app.UseStatusCodePagesWithReExecute("/not-found")` (it rewrites any 400–599 response without a body) and the admin sees the not-found page instead of an explanation; a 302 is outside that range and passes through. Still set `RejectionStatusCode = 429` before the redirect as a fallback for non-form callers (the default is 503). Partition by `HttpContext.Connection.RemoteIpAddress`, falling back to a single shared partition when it's null. Proxy caveat: behind Azure App Service the app only sees the real client IP when forwarded headers are processed (`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` app setting, or explicit `UseForwardedHeaders`); until then all clients share the front-end's IP — a stricter-not-looser failure mode, acceptable for this endpoint.

### Redirect handling for unauthorized access

`RedirectToLogin.razor` currently always sends to `/login`. Make it path-aware: if the current URI path starts with `/admin`, redirect to `/admin/login` instead (no returnUrl needed for v1 — after login just land on `/admin`).

(Route collisions with team codenames are no longer a concern — §0a moved team pages under `/team/`.)

### Pages

- `Components/Pages/Admin/AdminLogin.razor` — `@page "/admin/login"`, anonymous, `MainLayout` + `AuthCardLayout` card styled like `Login.razor` (distinct header, e.g. dark/warning "Admin sign-in"). Shows the "not configured" notice when applicable (inject `IOptions<AdminOptions>` — expose only `IsConfigured`, never the values).
- `Components/Layout/AdminLayout.razor` — minimal top bar: "Fotbalek Admin" brand, link(s) to admin sections, logout button (form POST to `/admin/auth/logout`), visually distinct from the team navbar (dark navbar) so it's obvious you're in admin mode.

---

## 3. Admin user management (v1)

`Components/Pages/Admin/Users.razor` — `@page "/admin"` (users list *is* the v1 dashboard; a separate overview page can come later), `@attribute [Authorize(Policy = AdminAuth.Policy)]`, `AdminLayout`.

### Users list

Table over all `AppUser`s (new `Services/AdminService.cs`, using `IDbContextFactory<AppDbContext>` like the other services for the list query, plus an injected `UserManager<AppUser>` for the reset flow), with search-by-username filter. Columns:

- Id, username, created at (via `TimeZoneService`)
- Teams (membership count), claimed players (count)
- Lockout badge if `LockoutEnd` is in the future
- Actions: **Reset password**

### Reset password flow

1. Button opens the shared `Modal` confirm: "Reset password for *user*? Their current password stops working immediately."
2. On confirm, `AdminService.ResetPasswordAsync(userId)`:
   - **Guard first**: re-verify the caller inside the service — read the circuit principal via `AuthenticationStateProvider` (same injection pattern as `CurrentUserService`) and fail unless it carries the admin claim. The page's `[Authorize(Policy)]` already gates rendering, but every team service re-checks the actor at the service layer (`team.AdminUserId != actorUserId` checks in `PlayerService`/`TeamService`/…); the one service that can reset any user's password must not be the one that skips that discipline.
   - Generate a short temp password: 10 chars from an unambiguous alphabet (`23456789ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz`, i.e. no `0/O/1/l/I`) using `RandomNumberGenerator`. Satisfies the length-6 policy; no other rules are enabled.
   - `var token = await userManager.GeneratePasswordResetTokenAsync(user); await userManager.ResetPasswordAsync(user, token, tempPassword);`
   - `await userManager.UpdateSecurityStampAsync(user);` — force-invalidates the user's existing cookies/circuits (belt-and-braces; reset already rotates the stamp, this makes the intent explicit).
   - Clear lockout if set (`SetLockoutEndDateAsync(user, null)`) and reset the failed-attempt counter (`ResetAccessFailedCountAsync(user)`) so the user can actually log in with the temp password.
   - Return the temp password to the component.
3. Modal switches to a result state: temp password in a monospace box with a copy button, and the note "Shown only once — send it to the user through a trusted channel." **Never log the temp password.**
4. User logs in with the temp password and changes it on `/account` (feature 1). No email/automation — handover is manual by design.

### Forgot-password hint on the login page

`Login.razor` currently ends with a dead-end line: "Forgot your password? Contact the service administrator." Now that a reset process exists, make it actionable:

- Inject `IOptions<AdminOptions>` and expose only `ContactEmail`.
- When `ContactEmail` is set: `Forgot your password? <a href="mailto:{email}?subject=Fotbalek%20password%20reset">Contact the administrator</a> to have it reset.`
- When not set: keep the current plain text as the fallback.
- Note: this address is rendered to anonymous visitors, so `Admin:ContactEmail` must be an address the admins are happy to expose publicly (a shared/alias mailbox rather than a personal one, if that matters).

---

## File plan

| File | Change |
| --- | --- |
| `Configuration/AdminOptions.cs` | new — options class above |
| `Constants.cs` or `Configuration/AdminAuth.cs` | new — scheme/policy/claim constants |
| `Endpoints/AdminEndpoints.cs` | new — admin login/logout + rate limiter |
| `Endpoints/AccountEndpoints.cs` | add `POST /account/change-password` |
| `Services/AdminService.cs` | new — list users (+counts), reset password |
| `Services/IdentityRevalidatingAuthenticationStateProvider.cs` | admin-claim exception in `user is null` branch |
| `Program.cs` | bind `AdminOptions`, add admin cookie scheme + policy + hardened default policy, merge middleware, rate limiter, register `AdminService`, map admin endpoints |
| `Components/Shared/RedirectToLogin.razor` | `/admin/*` → `/admin/login` |
| `Components/Pages/Account.razor` | new — feature 1 |
| `Components/Pages/Admin/AdminLogin.razor` | new |
| `Components/Pages/Admin/Users.razor` | new — `@page "/admin"` |
| `Components/Layout/AdminLayout.razor` | new |
| `Components/Layout/TeamLayout.razor`, `Components/Pages/Home.razor` | "My account" links |
| `Components/Pages/Login.razor` | forgot-password hint → mailto link when `Admin:ContactEmail` is set |
| `appsettings.json` | empty `Admin` section (incl. `ContactEmail`) |

Phase 0 (touched before the features):

| File | Change |
| --- | --- |
| 13 team-page components + shared link components (~20 files) | routes + links under `/team/{codename}` (§0a) |
| `Services/TeamAccessService.cs` | parse `/team/{code}`; delete reserved-word list (§0a); `IsAdminAsync` → `IsCaptainAsync` (§0b) |
| `Components/Pages/LegacyTeamRedirect.razor` | new — `/{code}` and `/{code}/{*rest}` → `/team/…` (§0a); **later removed** as unnecessary backwards-compat — unmatched paths now 404 via the router's `NotFoundPage` (§0a note) |
| `Data/Entities/Team.cs`, `Data/Entities/AppUser.cs`, `Data/AppDbContext.cs` + rename migration | `AdminUserId` → `CaptainUserId`, `AdministeredTeams` → `CaptainedTeams`, FK configuration (§0b) |
| `TeamService`, `PlayerService`, `MatchService`, `SeasonService`, `TeamLayout`, team pages | captain rename in code + UI strings (§0b) |

The only DB migration is the §0b column rename; the admin/account features themselves require no schema change.

## Implementation order

1. §0a route prefix (+ legacy redirect) — verify every team page still works at `/team/{code}` and an old `/{code}/matches` URL redirects.
2. §0b captain rename (+ rename migration) — verify claim-captain flow and settings gating still work.
3. `AdminOptions` + auth plumbing in `Program.cs` (scheme, policy + hardened default policy, merge middleware, revalidation fix) — verify app boots with and without config set.
4. Admin endpoints + `AdminLogin.razor` + `AdminLayout` + `RedirectToLogin` change — verify login/logout, not-configured notice, and that `/admin` bounces anonymous visitors to `/admin/login`.
5. `AdminService` + `Users.razor` with reset-password modal.
6. `/account` page + change-password endpoint + nav links.
7. Manual test matrix: not-configured admin, wrong admin creds, rate-limited admin login redirects back with the too-many-attempts message (not the not-found page), admin+user dual login, admin-only session visiting `/`, `/create`, `/account` (must behave as unauthenticated), temp password login → change on `/account`, old session invalidated after reset (allow up to 30 minutes — both the circuit revalidation loop and the cookie security-stamp validator run on 30-minute intervals, so invalidation is not instant; don't misread the delay as a failed test), legacy `/{code}` URLs redirect to `/team/{code}`, login page shows the mailto hint only when `Admin:ContactEmail` is set.

## Explicitly out of scope (later ideas)

- `MustChangePassword` flag forcing a change on first login after reset (needs a migration + login redirect).
- More admin tools: teams list, orphaned-player cleanup, delete/lock user, app-wide stats dashboard at `/admin` with users list moving to `/admin/users`.
- Audit log of admin actions.
