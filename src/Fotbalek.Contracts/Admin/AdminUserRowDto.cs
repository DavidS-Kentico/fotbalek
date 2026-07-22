namespace Fotbalek.Contracts.Admin;

/// <summary>One row of the admin user list.</summary>
public record AdminUserRowDto(
    int Id,
    string? UserName,
    DateTimeOffset CreatedAt,
    int TeamCount,
    int PlayerCount,
    DateTimeOffset? LockoutEnd);
