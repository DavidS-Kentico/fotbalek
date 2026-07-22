using Fotbalek.SharedKernel;

namespace Fotbalek.Application.Common;

/// <summary>Errors shared across features.</summary>
public static class CommonErrors
{
    public static readonly Error NotAuthenticated =
        Error.Unauthorized("Auth.NotAuthenticated", "You must be logged in.");

    public static readonly Error NotMember =
        Error.Forbidden("Teams.NotMember", "You are not a member of this team.");

    public static readonly Error NotCaptain =
        Error.Forbidden("Teams.NotCaptain", "Only the team captain can perform this action.");

    public static readonly Error NotAdmin =
        Error.Forbidden("Admin.NotAdmin", "Only the global admin can perform this action.");
}
