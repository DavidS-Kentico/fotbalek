using Microsoft.AspNetCore.Identity;

namespace Fotbalek.Web.Helpers;

public static class PasswordHasher
{
    private static readonly PasswordHasher<object> Hasher = new();
    private static readonly object DummyUser = new();

    public static string Hash(string password)
    {
        return Hasher.HashPassword(DummyUser, password);
    }

    public static bool Verify(string password, string hash)
    {
        var result = Hasher.VerifyHashedPassword(DummyUser, hash, password);
        return result == PasswordVerificationResult.Success ||
               result == PasswordVerificationResult.SuccessRehashNeeded;
    }
}
