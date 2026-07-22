using Fotbalek.Application.Common.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace Fotbalek.Infrastructure.Identity;

/// <summary>
/// Wraps Identity's PasswordHasher (its user type parameter is unused by the hash) so existing
/// Team.PasswordHash values keep verifying — do not switch algorithms.
/// </summary>
public sealed class IdentityTeamPasswordHasher : ITeamPasswordHasher
{
    private static readonly PasswordHasher<object> Hasher = new();
    private static readonly object DummyUser = new();

    public string Hash(string password) => Hasher.HashPassword(DummyUser, password);

    public bool Verify(string password, string hash)
    {
        var result = Hasher.VerifyHashedPassword(DummyUser, hash, password);
        return result == PasswordVerificationResult.Success ||
               result == PasswordVerificationResult.SuccessRehashNeeded;
    }
}
