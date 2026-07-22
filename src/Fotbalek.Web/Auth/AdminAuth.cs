namespace Fotbalek.Web.Auth;

/// <summary>
/// Constants for the config-based global admin authentication: a cookie scheme parallel to
/// the Identity application cookie, a claim marking the admin identity, and the policy that
/// gates admin pages. The admin identity never carries <c>ClaimTypes.NameIdentifier</c> —
/// it must not masquerade as an app user.
/// </summary>
public static class AdminAuth
{
    public const string Scheme = "AdminCookie";
    public const string Policy = "AdminOnly";
    public const string ClaimType = "fotbalek:admin";

    /// <summary>Rate-limiter policy name for the admin login endpoint.</summary>
    public const string LoginRateLimiterPolicy = "admin-login";
}
