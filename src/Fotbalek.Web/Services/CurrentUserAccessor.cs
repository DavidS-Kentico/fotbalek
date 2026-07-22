using System.Security.Claims;
using Fotbalek.Web.Auth;
using Microsoft.AspNetCore.Components.Authorization;

namespace Fotbalek.Web.Services;

/// <summary>Snapshot of the circuit's user, read from claims only — no DB round trip.</summary>
public sealed record CurrentUserInfo(int? UserId, string? UserName, bool IsAdmin)
{
    public bool IsAuthenticated => UserId != null;
}

/// <summary>
/// Circuit-scoped access to the current user's identity, replacing the old CurrentUserService:
/// components only ever need id + display name, both of which live on the Identity cookie's
/// claims (entities never reach components — §2).
/// </summary>
public class CurrentUserAccessor(AuthenticationStateProvider authStateProvider)
{
    public async Task<CurrentUserInfo> GetAsync()
    {
        var authState = await authStateProvider.GetAuthenticationStateAsync();
        var principal = authState.User;
        if (principal?.Identity?.IsAuthenticated != true)
            return new CurrentUserInfo(null, null, false);

        return new CurrentUserInfo(
            ScopedDispatcher.ParseUserId(principal),
            principal.Identity?.Name,
            principal.HasClaim(AdminAuth.ClaimType, "true"));
    }

    public async Task<int?> GetUserIdAsync() => (await GetAsync()).UserId;
}
