using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Contracts.Matches;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Matches;

public sealed record GetMatchesByPlayerQuery(int PlayerId, int Count = 10, int? SeasonId = null) : IQuery<List<MatchDto>>;

internal sealed class GetMatchesByPlayerQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<GetMatchesByPlayerQuery, List<MatchDto>>
{
    public async Task<Result<List<MatchDto>>> Handle(GetMatchesByPlayerQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberOfPlayerTeamAsync(query.PlayerId, cancellationToken))
            return Result.Failure<List<MatchDto>>(CommonErrors.NotMember);

        var list = await db.Matches
            .AsNoTracking()
            .Where(m => m.MatchPlayers.Any(mp => mp.PlayerId == query.PlayerId) &&
                        (query.SeasonId == null || m.SeasonId == query.SeasonId))
            .Include(m => m.MatchPlayers).ThenInclude(mp => mp.Player)
            .OrderByDescending(m => m.PlayedAt)
            .Take(query.Count)
            .ToListAsync(cancellationToken);
        return list.Select(m => m.ToDto()).ToList();
    }
}
