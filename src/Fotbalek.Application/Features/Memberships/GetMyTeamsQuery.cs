using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Features.Teams;
using Fotbalek.Contracts.Teams;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Memberships;

/// <summary>The CURRENT USER's teams, oldest membership first (team switcher, home page).</summary>
public sealed record GetMyTeamsQuery : IQuery<List<TeamDto>>;

internal sealed class GetMyTeamsQueryHandler(IAppDbContext db, IUserContext userContext)
    : IQueryHandler<GetMyTeamsQuery, List<TeamDto>>
{
    public async Task<Result<List<TeamDto>>> Handle(GetMyTeamsQuery query, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not int userId)
            return new List<TeamDto>();
        var teams = await db.TeamMemberships
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.JoinedAt)
            .Select(m => m.Team)
            .ToListAsync(cancellationToken);
        return teams.Select(t => t.ToDto()).ToList();
    }
}
