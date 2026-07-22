namespace Fotbalek.Contracts.Account;

/// <summary>The account page's header facts about the current user.</summary>
public record AccountInfoDto(int Id, string? UserName, DateTimeOffset CreatedAt);
