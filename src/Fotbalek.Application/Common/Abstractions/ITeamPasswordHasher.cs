namespace Fotbalek.Application.Common.Abstractions;

/// <summary>
/// Hashing for TEAM passwords (named to avoid confusion with Identity's IPasswordHasher&lt;TUser&gt;,
/// which guards user accounts). Implemented in Infrastructure over Identity's PasswordHasher so
/// existing Team.PasswordHash values keep verifying.
/// </summary>
public interface ITeamPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}
