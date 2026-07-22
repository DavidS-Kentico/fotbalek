using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Contracts.Matches;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Matches;

public sealed record GetRecentMatchesQuery(int TeamId, int Count = 10, int? SeasonId = null) : IQuery<List<MatchDto>>;

internal sealed class GetRecentMatchesQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<GetRecentMatchesQuery, List<MatchDto>>
{
    public async Task<Result<List<MatchDto>>> Handle(GetRecentMatchesQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberAsync(query.TeamId, cancellationToken))
            return Result.Failure<List<MatchDto>>(CommonErrors.NotMember);

        var list = await db.Matches
            .AsNoTracking()
            .Where(m => m.TeamId == query.TeamId && (query.SeasonId == null || m.SeasonId == query.SeasonId))
            .Include(m => m.MatchPlayers).ThenInclude(mp => mp.Player)
            .OrderByDescending(m => m.PlayedAt)
            .Take(query.Count)
            .ToListAsync(cancellationToken);
        return list.Select(m => m.ToDto()).ToList();
    }
}
