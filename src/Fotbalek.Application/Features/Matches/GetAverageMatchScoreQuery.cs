using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Matches;

public sealed record GetAverageMatchScoreQuery(int TeamId, int? SeasonId = null) : IQuery<double>;

internal sealed class GetAverageMatchScoreQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<GetAverageMatchScoreQuery, double>
{
    public async Task<Result<double>> Handle(GetAverageMatchScoreQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberAsync(query.TeamId, cancellationToken))
            return Result.Failure<double>(CommonErrors.NotMember);

        var totals = await db.Matches
            .Where(m => m.TeamId == query.TeamId && (query.SeasonId == null || m.SeasonId == query.SeasonId))
            .Select(m => m.Team1Score + m.Team2Score)
            .ToListAsync(cancellationToken);
        return totals.Count > 0 ? totals.Average() : 0;
    }
}
