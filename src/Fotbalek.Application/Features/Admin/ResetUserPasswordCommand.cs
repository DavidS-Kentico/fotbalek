using System.Security.Cryptography;
using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Domain.Entities;
using Fotbalek.SharedKernel;
using Microsoft.AspNetCore.Identity;

namespace Fotbalek.Application.Features.Admin;

/// <summary>
/// Resets the user's password to a generated temporary one and returns it. The admin hands it
/// over manually; it is never logged or persisted in plain text. Re-verifies the actor via
/// IUserContext.IsAdmin — a handler that can reset any user's password must not skip the
/// actor check.
/// </summary>
public sealed record ResetUserPasswordCommand(int UserId) : ICommand<string>;

internal sealed class ResetUserPasswordCommandHandler(
    UserManager<AppUser> userManager,
    IUserContext userContext)
    : ICommandHandler<ResetUserPasswordCommand, string>
{
    // No 0/O/1/l/I — the admin reads this to the user over a trusted channel, so every
    // character must be unambiguous. 10 chars satisfies the length-6 policy; no other
    // password rules are enabled.
    private const string TempPasswordAlphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz";
    private const int TempPasswordLength = 10;

    public async Task<Result<string>> Handle(ResetUserPasswordCommand command, CancellationToken cancellationToken)
    {
        if (!userContext.IsAdmin)
            return Result.Failure<string>(CommonErrors.NotAdmin);

        var user = await userManager.FindByIdAsync(command.UserId.ToString());
        if (user is null)
            return Result.Failure<string>(Error.NotFound("Admin.UserNotFound", "User not found."));

        var tempPassword = GenerateTempPassword();

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, tempPassword);
        if (!result.Succeeded)
            return Result.Failure<string>(Error.Failure(
                "Admin.ResetFailed", string.Join("; ", result.Errors.Select(e => e.Description))));

        // Force-invalidate the user's existing cookies/circuits (belt-and-braces; the reset
        // already rotates the stamp, this makes the intent explicit).
        await userManager.UpdateSecurityStampAsync(user);

        // Clear lockout and the failed-attempt counter so the user can actually log in
        // with the temp password.
        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);

        return tempPassword;
    }

    private static string GenerateTempPassword() =>
        new(RandomNumberGenerator.GetItems<char>(TempPasswordAlphabet, TempPasswordLength));
}
