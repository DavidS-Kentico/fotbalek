using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Matches;

public sealed record GetMatchesThisWeekQuery(int TeamId, int? SeasonId = null) : IQuery<int>;

internal sealed class GetMatchesThisWeekQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<GetMatchesThisWeekQuery, int>
{
    public async Task<Result<int>> Handle(GetMatchesThisWeekQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberAsync(query.TeamId, cancellationToken))
            return Result.Failure<int>(CommonErrors.NotMember);

        var recentActivityThreshold = DateTimeOffset.UtcNow.AddDays(-Constants.TimeThresholds.RecentActivityDays);
        return await db.Matches.CountAsync(
            m => m.TeamId == query.TeamId && m.PlayedAt >= recentActivityThreshold &&
                 (query.SeasonId == null || m.SeasonId == query.SeasonId),
            cancellationToken);
    }
}
