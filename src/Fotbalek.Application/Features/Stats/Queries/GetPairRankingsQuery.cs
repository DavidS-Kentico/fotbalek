using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Contracts.Stats;
using Fotbalek.Domain.Services;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Stats.Queries;

/// <summary>All-time pair rankings (min games threshold, win rate desc).</summary>
public sealed record GetPairRankingsQuery(int TeamId) : IQuery<List<PairStats>>;

internal sealed class GetPairRankingsQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<GetPairRankingsQuery, List<PairStats>>
{
    public async Task<Result<List<PairStats>>> Handle(GetPairRankingsQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberAsync(query.TeamId, cancellationToken))
            return Result.Failure<List<PairStats>>(CommonErrors.NotMember);

        var matches = await db.Matches
            .AsNoTracking()
            .Where(m => m.TeamId == query.TeamId)
            .Include(m => m.MatchPlayers)
                .ThenInclude(mp => mp.Player)
            .ToListAsync(cancellationToken);

        var playerById = matches
            .SelectMany(m => m.MatchPlayers)
            .Select(mp => mp.Player)
            .DistinctBy(p => p.Id)
            .ToDictionary(p => p.Id);

        return SeasonAggregates.ComputePairs(matches)
            .Where(kv => kv.Value.Matches >= Constants.TimeThresholds.MinGamesForPartnerStats)
            .Select(kv => new PairStats
            {
                Player1Id = kv.Key.Player1Id,
                Player1Name = playerById[kv.Key.Player1Id].Name,
                Player1AvatarId = playerById[kv.Key.Player1Id].AvatarId,
                Player2Id = kv.Key.Player2Id,
                Player2Name = playerById[kv.Key.Player2Id].Name,
                Player2AvatarId = playerById[kv.Key.Player2Id].AvatarId,
                Matches = kv.Value.Matches,
                Wins = kv.Value.Wins,
                Losses = kv.Value.Matches - kv.Value.Wins,
                WinRate = (double)kv.Value.Wins / kv.Value.Matches * 100,
                TotalScore = kv.Value.TotalScore,
                AverageScore = (double)kv.Value.TotalScore / kv.Value.Matches
            })
            .OrderByDescending(p => p.WinRate)
            .ThenByDescending(p => p.Matches)
            .ToList();
    }
}
