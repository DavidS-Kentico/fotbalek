using System.Security.Claims;
using Fotbalek.Web.Data.Entities;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;

namespace Fotbalek.Web.Services;

public class CurrentUserService(
    AuthenticationStateProvider authStateProvider,
    UserManager<AppUser> userManager)
{
    public async Task<AppUser?> GetUserAsync()
    {
        var authState = await authStateProvider.GetAuthenticationStateAsync();
        var principal = authState.User;
        if (principal?.Identity?.IsAuthenticated != true)
            return null;

        var idStr = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idStr, out var userId))
            return null;

        return await userManager.FindByIdAsync(userId.ToString());
    }

    public async Task<int?> GetUserIdAsync()
    {
        var authState = await authStateProvider.GetAuthenticationStateAsync();
        var idStr = authState.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(idStr, out var userId) ? userId : null;
    }
}
