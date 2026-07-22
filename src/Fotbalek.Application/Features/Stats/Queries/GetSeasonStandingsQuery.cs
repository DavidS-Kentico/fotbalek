using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Contracts.Stats;
using Fotbalek.Domain.Services;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Stats.Queries;

/// <summary>
/// Season standings. Active seasons aggregate on the fly from season matches (wins by score,
/// active players only, ranked by seasonal ELO with the deterministic tie-breaks). Closed
/// seasons render entirely from the frozen tables — zero aggregation; participants with
/// FinalRank == null (inactive at close) are hidden.
/// </summary>
public sealed record GetSeasonStandingsQuery(int SeasonId) : IQuery<List<SeasonStandingRow>>;

internal sealed class GetSeasonStandingsQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<GetSeasonStandingsQuery, List<SeasonStandingRow>>
{
    public async Task<Result<List<SeasonStandingRow>>> Handle(GetSeasonStandingsQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberOfSeasonTeamAsync(query.SeasonId, cancellationToken))
            return Result.Failure<List<SeasonStandingRow>>(CommonErrors.NotMember);

        var season = await db.Seasons.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == query.SeasonId, cancellationToken);
        if (season is null)
            return new List<SeasonStandingRow>();

        if (season.IsClosed)
        {
            var frozen = await db.SeasonPlayerResults
                .Where(r => r.SeasonPlayer.SeasonId == season.Id && r.FinalRank != null)
                .OrderBy(r => r.FinalRank)
                .Select(r => new SeasonStandingRow
                {
                    Rank = r.FinalRank!.Value,
                    PlayerId = r.SeasonPlayer.PlayerId,
                    PlayerName = r.SeasonPlayer.Player.Name,
                    AvatarId = r.SeasonPlayer.Player.AvatarId,
                    Elo = r.SeasonPlayer.Elo,
                    Matches = r.MatchesPlayed,
                    Wins = r.Wins,
                    Losses = r.Losses,
                    LongestWinStreak = r.LongestWinStreak,
                    LongestLossStreak = r.LongestLossStreak
                })
                .ToListAsync(cancellationToken);
            foreach (var row in frozen)
            {
                row.WinRate = row.Matches > 0 ? (double)row.Wins / row.Matches * 100 : 0;
            }
            return frozen;
        }

        var (ladder, aggregates) = await SeasonAggregateLoader.LoadLiveAsync(db, season.Id, cancellationToken);

        // Live ladder: seasonal ELO desc → wins desc → matches played desc → PlayerId asc.
        var rows = ladder
            .Where(sp => sp.Player.IsActive)
            .OrderByDescending(sp => sp.Elo)
            .ThenByDescending(sp => aggregates.TryGetValue(sp.PlayerId, out var a) ? a.Wins : 0)
            .ThenByDescending(sp => aggregates.TryGetValue(sp.PlayerId, out var a) ? a.MatchesPlayed : 0)
            .ThenBy(sp => sp.PlayerId)
            .Select((sp, index) =>
            {
                var agg = aggregates.TryGetValue(sp.PlayerId, out var a) ? a : new SeasonAggregates.ParticipantAggregate();
                return new SeasonStandingRow
                {
                    Rank = index + 1,
                    PlayerId = sp.PlayerId,
                    PlayerName = sp.Player.Name,
                    AvatarId = sp.Player.AvatarId,
                    Elo = sp.Elo,
                    Matches = agg.MatchesPlayed,
                    Wins = agg.Wins,
                    Losses = agg.Losses,
                    WinRate = agg.MatchesPlayed > 0 ? (double)agg.Wins / agg.MatchesPlayed * 100 : 0,
                    LongestWinStreak = agg.LongestWinStreak,
                    LongestLossStreak = agg.LongestLossStreak
                };
            })
            .ToList();
        return rows;
    }
}
