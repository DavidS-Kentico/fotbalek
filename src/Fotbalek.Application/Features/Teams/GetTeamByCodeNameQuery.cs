using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Contracts.Teams;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Teams;

/// <summary>Team lookup by code name — no membership requirement (pre-join pages).</summary>
public sealed record GetTeamByCodeNameQuery(string CodeName) : IQuery<TeamDto?>;

internal sealed class GetTeamByCodeNameQueryHandler(IAppDbContext db)
    : IQueryHandler<GetTeamByCodeNameQuery, TeamDto?>
{
    public async Task<Result<TeamDto?>> Handle(GetTeamByCodeNameQuery query, CancellationToken cancellationToken)
    {
        var codeName = query.CodeName.ToLowerInvariant();
        var team = await db.Teams.AsNoTracking()
            .FirstOrDefaultAsync(t => t.CodeName == codeName, cancellationToken);
        return team?.ToDto();
    }
}
