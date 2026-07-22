using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Seasons;

public sealed record GetMatchCountsBySeasonQuery(int TeamId) : IQuery<Dictionary<int, int>>;

internal sealed class GetMatchCountsBySeasonQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<GetMatchCountsBySeasonQuery, Dictionary<int, int>>
{
    public async Task<Result<Dictionary<int, int>>> Handle(GetMatchCountsBySeasonQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberAsync(query.TeamId, cancellationToken))
            return Result.Failure<Dictionary<int, int>>(CommonErrors.NotMember);

        return await db.Matches
            .Where(m => m.TeamId == query.TeamId && m.SeasonId != null)
            .GroupBy(m => m.SeasonId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
    }
}
