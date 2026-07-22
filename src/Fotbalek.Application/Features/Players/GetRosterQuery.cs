using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Application.Features.Seasons;
using Fotbalek.Application.Features.Stats;
using Fotbalek.Contracts.Players;
using Fotbalek.Domain.Entities;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Players;

/// <summary>
/// The roster page's full payload on the default lens: with an active season, ratings/ranks/
/// summaries and badges come from the season (ranked with the same deterministic tie-breaks as
/// the season standings, so the chips match the Rankings page); with no active season, all-time.
/// </summary>
public sealed record GetRosterQuery(int TeamId) : IQuery<RosterDto>;

internal sealed class GetRosterQueryHandler(IAppDbContext db, StatsEngine engine, TeamAccess teamAccess)
    : IQueryHandler<GetRosterQuery, RosterDto>
{
    public async Task<Result<RosterDto>> Handle(GetRosterQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberAsync(query.TeamId, cancellationToken))
            return Result.Failure<RosterDto>(CommonErrors.NotMember);

        var now = DateTimeOffset.UtcNow;
        var activeSeason = await db.Seasons
            .AsNoTracking()
            .Where(s => s.TeamId == query.TeamId)
            .Where(SeasonRules.ActiveAt(now))
            .FirstOrDefaultAsync(cancellationToken);

        if (activeSeason != null)
        {
            var (playersById, seasonMatches, seasonPlayers) = await engine.LoadSeasonAsync(query.TeamId, activeSeason.Id);
            var badges = engine.ComputeSeason(playersById, seasonPlayers, seasonMatches, isFullScope: true);
            var summaries = BuildSummaries(seasonMatches, seasonal: true);

            // Only players with a seasonal rating are ranked.
            var ranks = playersById.Values
                .Where(p => p.IsActive && seasonPlayers.ContainsKey(p.Id))
                .OrderByDescending(p => seasonPlayers[p.Id].Elo)
                .ThenByDescending(p => summaries.GetValueOrDefault(p.Id)?.Wins ?? 0)
                .ThenByDescending(p => summaries.GetValueOrDefault(p.Id)?.Games ?? 0)
                .ThenBy(p => p.Id)
                .Select((p, idx) => (p.Id, Rank: idx + 1))
                .ToDictionary(x => x.Id, x => x.Rank);

            return new RosterDto(
                playersById.Values.Select(p => p.ToDto()).ToList(),
                activeSeason.ToDto(),
                seasonPlayers.ToDictionary(kv => kv.Key, kv => kv.Value.Elo),
                summaries,
                ranks,
                badges);
        }
        else
        {
            var (playersById, matches) = await engine.LoadAsync(query.TeamId);
            var badges = engine.Compute(playersById, matches, isAllTime: true);
            var summaries = BuildSummaries(matches, seasonal: false);

            var ranks = playersById.Values
                .Where(p => p.IsActive)
                .OrderByDescending(p => p.Elo)
                .Select((p, idx) => (p.Id, Rank: idx + 1))
                .ToDictionary(x => x.Id, x => x.Rank);

            return new RosterDto(
                playersById.Values.Select(p => p.ToDto()).ToList(),
                null,
                [],
                summaries,
                ranks,
                badges);
        }
    }

    private static Dictionary<int, PlayerSummaryDto> BuildSummaries(IReadOnlyList<Match> matches, bool seasonal)
    {
        var acc = new Dictionary<int, (int Games, int Wins, int Losses, DateTimeOffset? LastPlayedAt, int LastEloChange)>();
        foreach (var match in matches)
        {
            foreach (var mp in match.MatchPlayers)
            {
                acc.TryGetValue(mp.PlayerId, out var s);
                s.Games++;
                var won = (mp.TeamNumber == 1 && match.Team1Score > match.Team2Score)
                       || (mp.TeamNumber == 2 && match.Team2Score > match.Team1Score);
                if (won) s.Wins++;
                else s.Losses++;
                if (s.LastPlayedAt == null || match.PlayedAt > s.LastPlayedAt)
                {
                    s.LastPlayedAt = match.PlayedAt;
                    // The trend chip shows the change on the lens's ladder.
                    s.LastEloChange = seasonal ? mp.SeasonEloChange ?? 0 : mp.EloChange;
                }
                acc[mp.PlayerId] = s;
            }
        }
        return acc.ToDictionary(
            kv => kv.Key,
            kv => new PlayerSummaryDto(kv.Value.Games, kv.Value.Wins, kv.Value.Losses, kv.Value.LastPlayedAt, kv.Value.LastEloChange));
    }
}
