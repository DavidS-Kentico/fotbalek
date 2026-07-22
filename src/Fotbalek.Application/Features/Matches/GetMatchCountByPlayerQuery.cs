using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Matches;

public sealed record GetMatchCountByPlayerQuery(int PlayerId, int? SeasonId = null) : IQuery<int>;

internal sealed class GetMatchCountByPlayerQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<GetMatchCountByPlayerQuery, int>
{
    public async Task<Result<int>> Handle(GetMatchCountByPlayerQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberOfPlayerTeamAsync(query.PlayerId, cancellationToken))
            return Result.Failure<int>(CommonErrors.NotMember);

        return await db.Matches.CountAsync(
            m => m.MatchPlayers.Any(mp => mp.PlayerId == query.PlayerId) &&
                 (query.SeasonId == null || m.SeasonId == query.SeasonId),
            cancellationToken);
    }
}
