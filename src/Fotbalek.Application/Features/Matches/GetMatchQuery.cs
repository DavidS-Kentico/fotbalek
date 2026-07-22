using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Contracts.Matches;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Matches;

public sealed record GetMatchQuery(int TeamId, int MatchId) : IQuery<MatchDto?>;

internal sealed class GetMatchQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<GetMatchQuery, MatchDto?>
{
    public async Task<Result<MatchDto?>> Handle(GetMatchQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberAsync(query.TeamId, cancellationToken))
            return Result.Failure<MatchDto?>(CommonErrors.NotMember);

        var match = await db.Matches
            .AsNoTracking()
            .Include(m => m.MatchPlayers).ThenInclude(mp => mp.Player)
            .Include(m => m.Season)
            .FirstOrDefaultAsync(m => m.Id == query.MatchId && m.TeamId == query.TeamId, cancellationToken);
        return match?.ToDto();
    }
}
