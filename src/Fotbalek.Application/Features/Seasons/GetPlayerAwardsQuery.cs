using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Contracts.Seasons;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Seasons;

/// <summary>All awards a player has earned, with their seasons — the permanent trophy case.</summary>
public sealed record GetPlayerAwardsQuery(int PlayerId) : IQuery<List<SeasonAwardDto>>;

internal sealed class GetPlayerAwardsQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<GetPlayerAwardsQuery, List<SeasonAwardDto>>
{
    public async Task<Result<List<SeasonAwardDto>>> Handle(GetPlayerAwardsQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberOfPlayerTeamAsync(query.PlayerId, cancellationToken))
            return Result.Failure<List<SeasonAwardDto>>(CommonErrors.NotMember);

        return await db.SeasonAwards
            .AsNoTracking()
            .Where(a => a.PlayerId == query.PlayerId)
            .OrderByDescending(a => a.Season.StartsAt)
            .ThenBy(a => a.Rank)
            .Select(a => new SeasonAwardDto(
                a.Id, a.SeasonId, a.Season.Name, a.Season.StartsAt,
                a.PlayerId, a.Player.Name, a.Player.AvatarId,
                a.Category, a.Rank,
                a.PartnerPlayerId,
                a.PartnerPlayer != null ? a.PartnerPlayer.Name : null,
                a.PartnerPlayer != null ? (int?)a.PartnerPlayer.AvatarId : null))
            .ToListAsync(cancellationToken);
    }
}
