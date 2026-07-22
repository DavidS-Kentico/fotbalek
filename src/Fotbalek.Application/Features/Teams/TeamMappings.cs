using Fotbalek.Contracts.Teams;
using Fotbalek.Domain.Entities;

namespace Fotbalek.Application.Features.Teams;

public static class TeamMappings
{
    public static TeamDto ToDto(this Team team) =>
        new(team.Id, team.Name, team.CodeName, team.CaptainUserId, team.CreatedAt);
}
