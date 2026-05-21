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
        if (user is null) return false;

        if (userManager.SupportsUserSecurityStamp)
        {
            var principalStamp = authenticationState.User.FindFirst(options.Value.ClaimsIdentity.SecurityStampClaimType)?.Value;
            var userStamp = await userManager.GetSecurityStampAsync(user);
            return principalStamp == userStamp;
        }
        return true;
    }
}
