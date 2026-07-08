using System.Security.Claims;
using System.Threading.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Fotbalek.Web.Components;
using Fotbalek.Web.Configuration;
using Fotbalek.Web.Data;
using Fotbalek.Web.Data.Entities;
using Fotbalek.Web.Endpoints;
using Fotbalek.Web.Game;
using Fotbalek.Web.Services;
using Fotbalek.Web.Services.Stats;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
// Factory pattern (recommended for Blazor Server): services create a short-lived DbContext per
// unit of work instead of sharing one circuit-scoped context for hours. AddDbContextFactory also
// registers AppDbContext itself as a scoped service, which Identity's AddEntityFrameworkStores
// and the startup migration below still rely on.
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

// Identity
builder.Services.AddIdentityCore<AppUser>(options =>
    {
        options.User.RequireUniqueEmail = false;
        options.Password.RequiredLength = 6;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireDigit = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
        options.SignIn.RequireConfirmedAccount = false;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole<int>>()
    .AddEntityFrameworkStores<AppDbContext>()
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
    // admin who is not logged in as an app user — and then misbehave when CurrentUserService
    // returns null. Identity cookie principals always carry NameIdentifier, so normal users
    // are unaffected; admin-only sessions get treated as unauthenticated on user-facing pages.
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

// App services
builder.Services.AddScoped<TeamService>();
builder.Services.AddScoped<PlayerService>();
builder.Services.AddScoped<MatchService>();
builder.Services.AddScoped<EloService>();
builder.Services.AddScoped<StatsService>();
builder.Services.AddScoped<ShareTokenService>();
builder.Services.AddScoped<TeamMembershipService>();
builder.Services.AddScoped<CurrentUserService>();
builder.Services.AddScoped<TeamAccessService>();
builder.Services.AddScoped<TimeZoneService>();
builder.Services.AddScoped<SeasonService>();
builder.Services.AddScoped<LandingStatsService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddSingleton<PresenceTracker>();
builder.Services.AddScoped<CircuitHandler, PresenceCircuitHandler>();
builder.Services.AddFoosballStats();

// Live game (in-memory foosball mini-game): rooms + dedicated hub for the JS canvas client.
builder.Services.AddSignalR();
builder.Services.AddSingleton<GameRoomManager>();

// Telemetry export (§12): ship OpenTelemetry — including the structured game-latency logs emitted by
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

// Apply migrations on startup (for PoC simplicity)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.Run();
