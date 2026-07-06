using System.Security.Cryptography;
using Fotbalek.Web.Configuration;
using Fotbalek.Web.Data;
using Fotbalek.Web.Data.Entities;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Web.Services;

/// <summary>
/// Global admin operations over the user store. Every method re-verifies the caller against
/// the circuit principal — the admin pages gate rendering with the admin policy, but the
/// service that can reset any user's password must not skip the service-layer actor check
/// the team services all apply.
/// </summary>
public class AdminService(
    IDbContextFactory<AppDbContext> dbFactory,
    UserManager<AppUser> userManager,
    AuthenticationStateProvider authStateProvider)
{
    // No 0/O/1/l/I — the admin reads this to the user over a trusted channel, so every
    // character must be unambiguous. 10 chars satisfies the length-6 policy; no other
    // password rules are enabled.
    private const string TempPasswordAlphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz";
    private const int TempPasswordLength = 10;

    public record UserRow(
        int Id,
        string? UserName,
        DateTimeOffset CreatedAt,
        int TeamCount,
        int PlayerCount,
        DateTimeOffset? LockoutEnd);

    public async Task<List<UserRow>> GetUsersAsync()
    {
        await EnsureAdminAsync();

        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Users
            .AsNoTracking()
            .OrderBy(u => u.UserName)
            .Select(u => new UserRow(
                u.Id,
                u.UserName,
                u.CreatedAt,
                u.Memberships.Count,
                u.Players.Count,
                u.LockoutEnd))
            .ToListAsync();
    }

    /// <summary>
    /// Resets the user's password to a generated temporary one and returns it. The caller
    /// hands it over manually; it is never logged or persisted in plain text.
    /// </summary>
    public async Task<string> ResetPasswordAsync(int userId)
    {
        await EnsureAdminAsync();

        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException("User not found.");

        var tempPassword = GenerateTempPassword();

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, tempPassword);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));

        // Force-invalidate the user's existing cookies/circuits (belt-and-braces; the reset
        // already rotates the stamp, this makes the intent explicit).
        await userManager.UpdateSecurityStampAsync(user);

        // Clear lockout and the failed-attempt counter so the user can actually log in
        // with the temp password.
        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);

        return tempPassword;
    }

    private async Task EnsureAdminAsync()
    {
        var authState = await authStateProvider.GetAuthenticationStateAsync();
        if (!authState.User.HasClaim(AdminAuth.ClaimType, "true"))
            throw new UnauthorizedAccessException("Only the global admin can perform this action.");
    }

    private static string GenerateTempPassword() =>
        new(RandomNumberGenerator.GetItems<char>(TempPasswordAlphabet, TempPasswordLength));
}
