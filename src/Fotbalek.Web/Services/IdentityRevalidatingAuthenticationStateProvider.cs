using System.Security.Claims;
using Fotbalek.Web.Configuration;
using Fotbalek.Web.Data.Entities;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Fotbalek.Web.Services;

/// <summary>
/// Revalidates the user's security stamp every 30 minutes so a logged-out cookie eventually invalidates.
/// </summary>
public class IdentityRevalidatingAuthenticationStateProvider(
    ILoggerFactory loggerFactory,
    IServiceScopeFactory scopeFactory,
    IOptions<IdentityOptions> options)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(30);

    protected override async Task<bool> ValidateAuthenticationStateAsync(
        Microsoft.AspNetCore.Components.Authorization.AuthenticationState authenticationState,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await userManager.GetUserAsync(authenticationState.User);
        if (user is null)
            // Admin-only circuits have no app user to validate — keep them alive. The
            // NameIdentifier guard keeps the dual-login edge correct: a principal carrying a
            // user identity whose account no longer exists must still die, or the stale user
            // identity would keep passing the hardened default policy.
            return authenticationState.User.HasClaim(AdminAuth.ClaimType, "true")
                && !authenticationState.User.HasClaim(c => c.Type == ClaimTypes.NameIdentifier);

        if (userManager.SupportsUserSecurityStamp)
        {
            var principalStamp = authenticationState.User.FindFirst(options.Value.ClaimsIdentity.SecurityStampClaimType)?.Value;
            var userStamp = await userManager.GetSecurityStampAsync(user);
            return principalStamp == userStamp;
        }
        return true;
    }
}
