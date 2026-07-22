using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Contracts.Seasons;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Seasons;

/// <summary>
/// Champion per closed season: the Player-gold award holder, or — when the season generated no
/// awards or no player reached the Player-award minimum — the FinalRank = 1 player from the
/// frozen standings.
/// </summary>
public sealed record GetSeasonChampionsQuery(int TeamId) : IQuery<Dictionary<int, SeasonChampionDto>>;

internal sealed class GetSeasonChampionsQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<GetSeasonChampionsQuery, Dictionary<int, SeasonChampionDto>>
{
    public async Task<Result<Dictionary<int, SeasonChampionDto>>> Handle(GetSeasonChampionsQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberAsync(query.TeamId, cancellationToken))
            return Result.Failure<Dictionary<int, SeasonChampionDto>>(CommonErrors.NotMember);

        var standingsChampions = await db.SeasonPlayerResults
            .Where(r => r.SeasonPlayer.Season.TeamId == query.TeamId && r.FinalRank == 1)
            .Select(r => new { r.SeasonPlayer.SeasonId, r.SeasonPlayer.PlayerId, r.SeasonPlayer.Player.Name, r.SeasonPlayer.Player.AvatarId })
            .ToListAsync(cancellationToken);

        var awardChampions = await db.SeasonAwards
            .Where(a => a.Season.TeamId == query.TeamId &&
                        a.Category == Constants.Seasons.AwardCategories.Player && a.Rank == 1)
            .Select(a => new { a.SeasonId, a.PlayerId, a.Player.Name, a.Player.AvatarId })
            .ToListAsync(cancellationToken);

        var result = standingsChampions.ToDictionary(
            x => x.SeasonId,
            x => new SeasonChampionDto(x.PlayerId, x.Name, x.AvatarId, IsAwardHolder: false));
        foreach (var a in awardChampions)
        {
            result[a.SeasonId] = new SeasonChampionDto(a.PlayerId, a.Name, a.AvatarId, IsAwardHolder: true);
        }
        return result;
    }
}
