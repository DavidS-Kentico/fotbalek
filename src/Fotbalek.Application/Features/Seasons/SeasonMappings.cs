using Fotbalek.Contracts.Seasons;
using Fotbalek.Domain.Entities;

namespace Fotbalek.Application.Features.Seasons;

public static class SeasonMappings
{
    public static SeasonDto ToDto(this Season season) =>
        new(season.Id, season.TeamId, season.Name, season.Description,
            season.StartsAt, season.EndsAt, season.ClosedAt, season.CreatedAt);
}
