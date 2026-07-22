using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Contracts.Seasons;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Seasons;

public sealed record GetSeasonsQuery(int TeamId) : IQuery<List<SeasonDto>>;

internal sealed class GetSeasonsQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<GetSeasonsQuery, List<SeasonDto>>
{
    public async Task<Result<List<SeasonDto>>> Handle(GetSeasonsQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberAsync(query.TeamId, cancellationToken))
            return Result.Failure<List<SeasonDto>>(CommonErrors.NotMember);

        var seasons = await db.Seasons
            .AsNoTracking()
            .Where(s => s.TeamId == query.TeamId)
            .OrderByDescending(s => s.StartsAt)
            .ThenByDescending(s => s.Id)
            .ToListAsync(cancellationToken);
        return seasons.Select(s => s.ToDto()).ToList();
    }
}
