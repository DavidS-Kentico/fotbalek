namespace Fotbalek.Contracts.Teams;

public record TeamDto(
    int Id,
    string Name,
    string CodeName,
    int? CaptainUserId,
    DateTimeOffset CreatedAt);
