namespace Fotbalek.Application.Common.Abstractions;

/// <summary>
/// The caller's identity for the current dispatch scope, seeded by the host (circuit auth state,
/// HttpContext.User, or hub Context.User). Handlers consume this — never ClaimsPrincipal.
/// Models the anonymous case: <see cref="UserId"/> is null for public pages.
/// </summary>
public interface IUserContext
{
    int? UserId { get; }

    /// <summary>The config-based global admin claim — never an app user id.</summary>
    bool IsAdmin { get; }
}
