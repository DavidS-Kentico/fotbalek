using Fotbalek.Web.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Fotbalek.Web.Endpoints;

public static class AccountEndpoints
{
    private const int UsernameMinLength = 3;
    private const int UsernameMaxLength = 30;

    public static void MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Antiforgery is enabled — forms must include <AntiforgeryToken /> in their markup.
        var group = endpoints.MapGroup("/account");

        group.MapPost("/login", async (
            [FromForm] string username,
            [FromForm] string password,
            [FromForm] string? returnUrl,
            SignInManager<AppUser> signInManager,
            HttpContext http) =>
        {
            username = (username ?? string.Empty).Trim();

            // SignInManager looks up by NormalizedUserName, so casing in the input doesn't matter.
            var result = await signInManager.PasswordSignInAsync(username, password, isPersistent: true, lockoutOnFailure: true);
            if (!result.Succeeded)
            {
                var errorCode = result.IsLockedOut ? "locked" : "1";
                var qs = $"?error={errorCode}&username={Uri.EscapeDataString(username)}";
                if (!string.IsNullOrEmpty(returnUrl))
                    qs += $"&returnUrl={Uri.EscapeDataString(returnUrl)}";
                return Results.Redirect("/login" + qs);
            }

            return Results.Redirect(SafeLocalRedirect(returnUrl));
        });

        group.MapPost("/register", async (
            [FromForm] string username,
            [FromForm] string password,
            [FromForm] string? returnUrl,
            UserManager<AppUser> userManager,
            SignInManager<AppUser> signInManager) =>
        {
            username = (username ?? string.Empty).Trim();

            if (!IsValidUsername(username, out var validationError))
            {
                return RedirectToRegister(validationError, username, returnUrl);
            }

            var user = new AppUser
            {
                UserName = username,
                CreatedAt = DateTimeOffset.UtcNow
            };
            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                var error = string.Join("; ", result.Errors.Select(e => e.Description));
                return RedirectToRegister(error, username, returnUrl);
            }

            await signInManager.SignInAsync(user, isPersistent: true);

            return Results.Redirect(SafeLocalRedirect(returnUrl));
        });

        group.MapPost("/logout", async (SignInManager<AppUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.Redirect("/login");
        });
    }

    private static bool IsValidUsername(string username, out string error)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            error = "Username is required.";
            return false;
        }
        if (username.Length is < UsernameMinLength or > UsernameMaxLength)
        {
            error = $"Username must be {UsernameMinLength}–{UsernameMaxLength} characters.";
            return false;
        }
        foreach (var c in username)
        {
            if (!(char.IsLetterOrDigit(c) || c is '.' or '_' or '-'))
            {
                error = "Username may only contain letters, digits, dot, dash, and underscore.";
                return false;
            }
        }
        error = string.Empty;
        return true;
    }

    private static IResult RedirectToRegister(string error, string username, string? returnUrl)
    {
        var qs = $"?error={Uri.EscapeDataString(error)}&username={Uri.EscapeDataString(username)}";
        if (!string.IsNullOrEmpty(returnUrl))
            qs += $"&returnUrl={Uri.EscapeDataString(returnUrl)}";
        return Results.Redirect("/register" + qs);
    }

    private static string SafeLocalRedirect(string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl)) return "/";
        // Reject absolute URLs and network-path references (//evil.com, /\evil.com) to prevent open redirects.
        if (returnUrl[0] != '/') return "/";
        if (returnUrl.Length > 1 && (returnUrl[1] == '/' || returnUrl[1] == '\\')) return "/";
        return Uri.IsWellFormedUriString(returnUrl, UriKind.Relative) ? returnUrl : "/";
    }
}
