using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Contracts.Players;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Players;

public sealed record GetTeamPlayersQuery(int TeamId, bool IncludeInactive = false) : IQuery<List<PlayerDto>>;

internal sealed class GetTeamPlayersQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<GetTeamPlayersQuery, List<PlayerDto>>
{
    public async Task<Result<List<PlayerDto>>> Handle(GetTeamPlayersQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberAsync(query.TeamId, cancellationToken))
            return Result.Failure<List<PlayerDto>>(CommonErrors.NotMember);

        var players = db.Players.AsNoTracking().Where(p => p.TeamId == query.TeamId);
        if (!query.IncludeInactive)
            players = players.Where(p => p.IsActive);
        var list = await players.OrderBy(p => p.Name).ToListAsync(cancellationToken);
        return list.Select(p => p.ToDto()).ToList();
    }
}
