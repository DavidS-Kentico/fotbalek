using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Contracts.Stats;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Stats.Queries;

/// <summary>All-time team rankings by ELO (active players).</summary>
public sealed record GetRankingsQuery(int TeamId) : IQuery<List<PlayerRanking>>;

internal sealed class GetRankingsQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<GetRankingsQuery, List<PlayerRanking>>
{
    public async Task<Result<List<PlayerRanking>>> Handle(GetRankingsQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberAsync(query.TeamId, cancellationToken))
            return Result.Failure<List<PlayerRanking>>(CommonErrors.NotMember);

        var players = await db.Players
            .AsNoTracking()
            .Where(p => p.TeamId == query.TeamId && p.IsActive)
            .OrderByDescending(p => p.Elo)
            .ToListAsync(cancellationToken);

        if (players.Count == 0)
            return new List<PlayerRanking>();

        var playerIds = players.Select(p => p.Id).ToList();

        var matchStats = await db.MatchPlayers
            .Where(mp => playerIds.Contains(mp.PlayerId))
            .GroupBy(mp => mp.PlayerId)
            .Select(g => new
            {
                PlayerId = g.Key,
                MatchCount = g.Count(),
                // Wins are determined by score, not ELO-change sign: a winner's change rounds to 0
                // at ELO gaps ≳720 (see StatHelpers.IsWinner). Mirrors GetRosterQuery's win rule.
                Wins = g.Count(mp =>
                    (mp.TeamNumber == 1 && mp.Match.Team1Score > mp.Match.Team2Score) ||
                    (mp.TeamNumber == 2 && mp.Match.Team2Score > mp.Match.Team1Score))
            })
            .ToDictionaryAsync(x => x.PlayerId, cancellationToken);

        var rankings = new List<PlayerRanking>();
        var rank = 1;

        foreach (var player in players)
        {
            var stats = matchStats.GetValueOrDefault(player.Id);
            var matchCount = stats?.MatchCount ?? 0;
            var wins = stats?.Wins ?? 0;

            rankings.Add(new PlayerRanking
            {
                Rank = rank++,
                PlayerId = player.Id,
                PlayerName = player.Name,
                AvatarId = player.AvatarId,
                Elo = player.Elo,
                Matches = matchCount,
                Wins = wins,
                WinRate = matchCount > 0 ? (double)wins / matchCount * 100 : 0
            });
        }

        return rankings;
    }
}
