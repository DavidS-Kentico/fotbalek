using System.Security.Claims;
using System.Threading.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Fotbalek.Application;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Domain.Entities;
using Fotbalek.Infrastructure;
using Fotbalek.Infrastructure.Persistence;
using Fotbalek.Web.Auth;
using Fotbalek.Web.Components;
using Fotbalek.Web.Endpoints;
using Fotbalek.Web.Game;
using Fotbalek.Web.Realtime;
using Fotbalek.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Layered registrations: Application (mediator pipeline, validators, stats engine, scoped
// UserContext/EventCollector — scanning this assembly too so the chat event bridge registers,
// §4.2) and Infrastructure (scoped AppDbContext, Identity core + stores, locks, hasher).
builder.Services.AddApplication(typeof(Program).Assembly);
builder.Services.AddInfrastructure(builder.Configuration);

// SignInManager and the default token providers live in the ASP.NET shared framework, which
// Infrastructure (a plain class library) can't reference — layer them on via a fresh
// IdentityBuilder over the same service collection (§4.6). Token providers are needed by
// admin password reset.
new IdentityBuilder(typeof(AppUser), typeof(IdentityRole<int>), builder.Services)
    .AddSignInManager()
    .AddDefaultTokenProviders();

// Global admin: config-based credentials on a cookie scheme parallel to the Identity one.
// The admin principal never enters the Identity user store.
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.SectionName));

// AddIdentityCookies() returns IdentityCookiesBuilder, not AuthenticationBuilder —
// AddCookie cannot be chained after it; keep the builder in a local.
var authBuilder = builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme);
authBuilder.AddIdentityCookies();
authBuilder.AddCookie(AdminAuth.Scheme, opt =>
{
    opt.Cookie.Name = "fotbalek.admin";
    opt.LoginPath = "/admin/login";
    opt.AccessDeniedPath = "/admin/login";
    opt.ExpireTimeSpan = TimeSpan.FromHours(8);
    opt.SlidingExpiration = false;
});

builder.Services.ConfigureApplicationCookie(opt =>
{
    opt.LoginPath = "/login";
    opt.LogoutPath = "/account/logout";
    opt.AccessDeniedPath = "/login";
    opt.ExpireTimeSpan = TimeSpan.FromDays(30);
    opt.SlidingExpiration = true;
});

builder.Services.AddAuthorizationBuilder()
    // The scheme binding matters at the HTTP layer: [Authorize(Policy)] on /admin is enforced
    // by endpoint authorization before Blazor renders, and without any scheme the challenge
    // goes to the default (Identity) scheme's /login instead of /admin/login. Both schemes
    // must be listed: a scheme-bound policy REPLACES HttpContext.User with only those schemes'
    // identities for the request, so binding AdminCookie alone would drop the user identity
    // from /admin page loads under a dual (admin + user) session — antiforgery tokens rendered
    // there would bind to the admin-only claim set and fail on POST against the dual principal.
    // Order matters too: on challenge/forbid each scheme redirects in turn and the last one
    // wins, so AdminCookie last keeps unauthorized visitors landing on /admin/login.
    .AddPolicy(AdminAuth.Policy, p => p
        .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme, AdminAuth.Scheme)
        .RequireClaim(AdminAuth.ClaimType, "true"))
    // Default-policy hardening: an admin-only principal IS authenticated, so without this
    // every bare [Authorize] page (Create, Join, team pages, /account) would render for an
    // admin who is not logged in as an app user — and then misbehave when the current-user
    // accessor returns null. Identity cookie principals always carry NameIdentifier, so normal
    // users are unaffected; admin-only sessions get treated as unauthenticated on user-facing pages.
    .SetDefaultPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireClaim(ClaimTypes.NameIdentifier)
        .Build());

// Config credentials get no Identity lockout protection — this fixed window is the only
// brute-force guard on the admin login endpoint.
builder.Services.AddRateLimiter(options =>
{
    // Fallback for non-form callers; the redirect below overrides it for browsers.
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) =>
    {
        // A bodiless 429 would be re-executed by UseStatusCodePagesWithReExecute("/not-found")
        // and the admin would see the not-found page; a 302 is outside the 400–599 range.
        context.HttpContext.Response.Redirect("/admin/login?error=rate");
        return ValueTask.CompletedTask;
    };
    options.AddPolicy(AdminAuth.LoginRateLimiterPolicy, ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            // Behind a proxy all clients share the front-end IP until forwarded headers are
            // enabled — stricter, not looser; acceptable for this endpoint.
            ctx.Connection.RemoteIpAddress?.ToString() ?? "shared",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

// Web host services: the scope-per-dispatch entry point (§4.3) and the circuit-level helpers.
builder.Services.AddSingleton<ScopedDispatcher>();
builder.Services.AddScoped<IScopedSender, ScopedSender>();
builder.Services.AddScoped<CurrentUserAccessor>();
builder.Services.AddScoped<CurrentTeamProvider>();
builder.Services.AddScoped<TimeZoneService>();

// Presence: in-memory singleton + per-circuit tracking handler (unchanged, §4.4).
builder.Services.AddSingleton<PresenceTracker>();
builder.Services.AddScoped<CircuitHandler, PresenceCircuitHandler>();

// Team chat realtime: in-process pub/sub singleton (fed by the post-commit event bridge),
// per-circuit dock UI state, and the Web-only typing path.
builder.Services.AddSingleton<ChatNotifier>();
builder.Services.AddScoped<ChatUiState>();
builder.Services.AddScoped<ChatTypingService>();

// Live game (in-memory foosball mini-game): rooms + dedicated hub for the JS canvas client.
builder.Services.AddSignalR();
builder.Services.AddSingleton<GameRoomManager>();

// Telemetry export: ship OpenTelemetry — including the structured game-latency logs emitted by
// GameTelemetry — to the existing Application Insights resource via the Azure Monitor distro.
// Gated behind a flag AND a present connection string so local dev and undecided deploys are untouched.
// IMPORTANT: this in-process distro competes with App Service *codeless* auto-instrumentation. When you
// turn this on, also set the app setting `ApplicationInsightsAgent_EXTENSION_VERSION=disabled` in Azure
// so the two don't fight over the same sources (Microsoft's guidance for code-based instrumentation).
// The distro also collects requests/dependencies/logs, so nothing is lost by the switch.
var enableOtel = builder.Configuration.GetValue<bool>("Telemetry:UseAzureMonitorOpenTelemetry");
var appInsightsConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
if (enableOtel && !string.IsNullOrWhiteSpace(appInsightsConnectionString))
{
    builder.Services.AddOpenTelemetry()
        .UseAzureMonitor(o => o.ConnectionString = appInsightsConnectionString);
}

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseRateLimiter();

app.UseAuthentication();
// UseAuthentication populates HttpContext.User from the default (Identity) scheme only, and
// Blazor's cascading AuthenticationState is seeded from HttpContext.User at circuit start —
// without this merge the admin cookie would be invisible to AuthorizeView/AuthorizeRouteView.
// An admin can simultaneously be a normal user: both identities coexist on the principal.
app.Use(async (ctx, next) =>
{
    var admin = await ctx.AuthenticateAsync(AdminAuth.Scheme);
    if (admin.Succeeded && admin.Principal != null)
        ctx.User.AddIdentities(admin.Principal.Identities);
    await next();
});
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAccountEndpoints();
app.MapAdminEndpoints();
app.MapHub<GameHub>("/hubs/game");

// Apply migrations on startup (for PoC simplicity) — the composition-root exception to
// "Web never touches AppDbContext" (§2).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.Run();
