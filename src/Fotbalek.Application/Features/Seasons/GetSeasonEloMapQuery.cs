using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Seasons;

/// <summary>Seasonal ELO per player for matchmaking. Players without a row default to 1000 at the call site.</summary>
public sealed record GetSeasonEloMapQuery(int SeasonId) : IQuery<Dictionary<int, int>>;

internal sealed class GetSeasonEloMapQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<GetSeasonEloMapQuery, Dictionary<int, int>>
{
    public async Task<Result<Dictionary<int, int>>> Handle(GetSeasonEloMapQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberOfSeasonTeamAsync(query.SeasonId, cancellationToken))
            return Result.Failure<Dictionary<int, int>>(CommonErrors.NotMember);

        return await db.SeasonPlayers
            .Where(sp => sp.SeasonId == query.SeasonId)
            .ToDictionaryAsync(sp => sp.PlayerId, sp => sp.Elo, cancellationToken);
    }
}
