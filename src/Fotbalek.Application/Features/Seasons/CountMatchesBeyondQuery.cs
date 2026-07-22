using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Seasons;

/// <summary>Matches that would be unassigned by shrinking EndsAt — for the confirmation dialog.</summary>
public sealed record CountMatchesBeyondQuery(int SeasonId, DateTimeOffset NewEndsAt) : IQuery<int>;

internal sealed class CountMatchesBeyondQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<CountMatchesBeyondQuery, int>
{
    public async Task<Result<int>> Handle(CountMatchesBeyondQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberOfSeasonTeamAsync(query.SeasonId, cancellationToken))
            return Result.Failure<int>(CommonErrors.NotMember);

        return await db.Matches.CountAsync(
            m => m.SeasonId == query.SeasonId && m.PlayedAt >= query.NewEndsAt, cancellationToken);
    }
}
