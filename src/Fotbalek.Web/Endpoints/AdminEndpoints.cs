using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Fotbalek.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Fotbalek.Web.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Antiforgery is enabled — forms must include <AntiforgeryToken /> in their markup.
        var group = endpoints.MapGroup("/admin/auth");

        group.MapPost("/login", async (
            [FromForm] string username,
            [FromForm] string password,
            IOptions<AdminOptions> options,
            HttpContext http) =>
        {
            // Not-configured fails with the same generic error as bad credentials —
            // don't leak which case it is.
            if (!options.Value.IsConfigured)
            {
                return Results.Redirect("/admin/login?error=1");
            }

            // Evaluate both comparisons before branching — short-circuiting would let
            // response timing reveal whether the username alone matched.
            var usernameOk = FixedTimeEquals(username, options.Value.Username!);
            var passwordOk = FixedTimeEquals(password, options.Value.Password!);
            if (!usernameOk || !passwordOk)
            {
                return Results.Redirect("/admin/login?error=1");
            }

            // The authenticationType argument matters: without it the identity reports
            // IsAuthenticated == false, a silent trap for any future RequireAuthenticatedUser-
            // style requirement on the admin policy. No ClaimTypes.NameIdentifier — existing
            // code parses it as the app user id, and the admin identity must never
            // masquerade as an app user.
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, options.Value.Username!), new Claim(AdminAuth.ClaimType, "true")],
                AdminAuth.Scheme);
            await http.SignInAsync(AdminAuth.Scheme, new ClaimsPrincipal(identity));

            return Results.Redirect("/admin");
        }).RequireRateLimiting(AdminAuth.LoginRateLimiterPolicy);

        group.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(AdminAuth.Scheme);
            return Results.Redirect("/admin/login");
        });
    }

    // Hashing first equalizes lengths — FixedTimeEquals returns early on length mismatch —
    // so the comparison is constant-time regardless of input.
    private static bool FixedTimeEquals(string? provided, string expected)
    {
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided ?? string.Empty));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(providedHash, expectedHash);
    }
}
