namespace Fotbalek.Web.Auth;

/// <summary>
/// Global admin credentials from configuration (appsettings/user-secrets locally, host app
/// settings in production). The admin is authenticated against these values, never against
/// the Identity user store. When not configured, admin login is disabled but the app runs
/// normally.
/// </summary>
public class AdminOptions
{
    public const string SectionName = "Admin";

    public string? Username { get; set; }
    public string? Password { get; set; }

    // Public contact address shown on the login page (forgot-password hint).
    // Independent of the credentials: it can be set while admin login is not, and vice versa.
    public string? ContactEmail { get; set; }

    // Auth only — ContactEmail deliberately not included.
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
}
