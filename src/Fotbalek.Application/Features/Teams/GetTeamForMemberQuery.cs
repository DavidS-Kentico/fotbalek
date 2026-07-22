using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Contracts.Teams;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Teams;

/// <summary>
/// The team behind a /team/{code} URL, returned only when the CURRENT USER is a member —
/// null otherwise (used by the host's per-circuit team resolution).
/// </summary>
public sealed record GetTeamForMemberQuery(string CodeName) : IQuery<TeamDto?>;

internal sealed class GetTeamForMemberQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<GetTeamForMemberQuery, TeamDto?>
{
    public async Task<Result<TeamDto?>> Handle(GetTeamForMemberQuery query, CancellationToken cancellationToken)
    {
        var codeName = query.CodeName.ToLowerInvariant();
        var team = await db.Teams.AsNoTracking()
            .FirstOrDefaultAsync(t => t.CodeName == codeName, cancellationToken);
        if (team is null)
            return Result.Success<TeamDto?>(null);

        if (!await teamAccess.IsMemberAsync(team.Id, cancellationToken))
            return Result.Success<TeamDto?>(null);

        return team.ToDto();
    }
}
