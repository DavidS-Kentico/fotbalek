using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Contracts.Seasons;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Seasons;

public sealed record GetSeasonAwardsQuery(int SeasonId) : IQuery<List<SeasonAwardDto>>;

internal sealed class GetSeasonAwardsQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<GetSeasonAwardsQuery, List<SeasonAwardDto>>
{
    public async Task<Result<List<SeasonAwardDto>>> Handle(GetSeasonAwardsQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberOfSeasonTeamAsync(query.SeasonId, cancellationToken))
            return Result.Failure<List<SeasonAwardDto>>(CommonErrors.NotMember);

        return await db.SeasonAwards
            .AsNoTracking()
            .Where(a => a.SeasonId == query.SeasonId)
            .OrderBy(a => a.Category).ThenBy(a => a.Rank)
            .Select(a => new SeasonAwardDto(
                a.Id, a.SeasonId, null, null,
                a.PlayerId, a.Player.Name, a.Player.AvatarId,
                a.Category, a.Rank,
                a.PartnerPlayerId,
                a.PartnerPlayer != null ? a.PartnerPlayer.Name : null,
                a.PartnerPlayer != null ? (int?)a.PartnerPlayer.AvatarId : null))
            .ToListAsync(cancellationToken);
    }
}
