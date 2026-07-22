using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Contracts.Stats;
using Fotbalek.Domain.Services;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Stats.Queries;

/// <summary>
/// Season pair standings — wins by score, minimum games applied at render time, ordered with
/// the pair-award tie-break chain (win rate desc → matches desc → combined seasonal ELO desc →
/// smaller PlayerId asc). Frozen SeasonPair rows for closed seasons (pairs with a member
/// inactive at close hidden; no average score stored), live aggregation for the active one.
/// </summary>
public sealed record GetSeasonPairRankingsQuery(int SeasonId) : IQuery<List<PairStats>>;

internal sealed class GetSeasonPairRankingsQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<GetSeasonPairRankingsQuery, List<PairStats>>
{
    public async Task<Result<List<PairStats>>> Handle(GetSeasonPairRankingsQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberOfSeasonTeamAsync(query.SeasonId, cancellationToken))
            return Result.Failure<List<PairStats>>(CommonErrors.NotMember);

        var season = await db.Seasons.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == query.SeasonId, cancellationToken);
        if (season is null)
            return new List<PairStats>();

        var ladder = await db.SeasonPlayers
            .AsNoTracking()
            .Include(sp => sp.Player)
            .Where(sp => sp.SeasonId == season.Id)
            .ToListAsync(cancellationToken);
        var eloByPlayer = ladder.ToDictionary(sp => sp.PlayerId, sp => sp.Elo);
        var playerById = ladder.ToDictionary(sp => sp.PlayerId, sp => sp.Player);

        List<PairStats> pairs;
        if (season.IsClosed)
        {
            // Frozen view: what a closed season displays never depends on current IsActive — only
            // on the state frozen at close (FinalRank == null ⇔ inactive at close).
            var activeAtClose = (await db.SeasonPlayerResults
                    .Where(r => r.SeasonPlayer.SeasonId == season.Id && r.FinalRank != null)
                    .Select(r => r.SeasonPlayer.PlayerId)
                    .ToListAsync(cancellationToken))
                .ToHashSet();

            pairs = (await db.SeasonPairs
                    .AsNoTracking()
                    .Where(p => p.SeasonId == season.Id)
                    .Include(p => p.Player1)
                    .Include(p => p.Player2)
                    .ToListAsync(cancellationToken))
                .Where(p => activeAtClose.Contains(p.Player1Id) && activeAtClose.Contains(p.Player2Id))
                .Select(p => new PairStats
                {
                    Player1Id = p.Player1Id,
                    Player1Name = p.Player1.Name,
                    Player1AvatarId = p.Player1.AvatarId,
                    Player2Id = p.Player2Id,
                    Player2Name = p.Player2.Name,
                    Player2AvatarId = p.Player2.AvatarId,
                    Matches = p.MatchesTogether,
                    Wins = p.WinsTogether,
                    Losses = p.MatchesTogether - p.WinsTogether,
                    WinRate = p.MatchesTogether > 0 ? (double)p.WinsTogether / p.MatchesTogether * 100 : 0
                })
                .ToList();
        }
        else
        {
            var matches = await db.Matches
                .AsNoTracking()
                .Include(m => m.MatchPlayers)
                .Where(m => m.SeasonId == season.Id)
                .ToListAsync(cancellationToken);

            pairs = SeasonAggregates.ComputePairs(matches)
                .Where(kv => playerById.TryGetValue(kv.Key.Player1Id, out var p1) && p1.IsActive &&
                             playerById.TryGetValue(kv.Key.Player2Id, out var p2) && p2.IsActive)
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
                    WinRate = kv.Value.Matches > 0 ? (double)kv.Value.Wins / kv.Value.Matches * 100 : 0,
                    TotalScore = kv.Value.TotalScore,
                    AverageScore = kv.Value.Matches > 0 ? (double)kv.Value.TotalScore / kv.Value.Matches : 0
                })
                .ToList();
        }

        return pairs
            .Where(p => p.Matches >= Constants.TimeThresholds.MinGamesForPartnerStats)
            .OrderByDescending(p => p.WinRate)
            .ThenByDescending(p => p.Matches)
            .ThenByDescending(p => eloByPlayer.GetValueOrDefault(p.Player1Id) + eloByPlayer.GetValueOrDefault(p.Player2Id))
            .ThenBy(p => Math.Min(p.Player1Id, p.Player2Id))
            .ToList();
    }
}
