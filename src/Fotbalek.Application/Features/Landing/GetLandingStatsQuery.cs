using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Contracts.Landing;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Landing;

/// <summary>
/// Public landing-page statistics. Everything here is deliberately privacy-safe:
/// only cross-team aggregates and anonymized scores are exposed — never a team's
/// name/code or a player's identity — so a visitor can't learn which specific team
/// is most active or who played whom. Dispatched anonymously (null user id).
/// </summary>
public sealed record GetLandingStatsQuery : IQuery<LandingStatsDto>;

internal sealed class GetLandingStatsQueryHandler(IAppDbContext db)
    : IQueryHandler<GetLandingStatsQuery, LandingStatsDto>
{
    private const int RecentScoresLimit = 12;
    private const int ActivityWindowDays = 30;

    public async Task<Result<LandingStatsDto>> Handle(GetLandingStatsQuery query, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var weekAgo = now.AddDays(-Constants.TimeThresholds.RecentActivityDays);
        var windowStart = now.Date.AddDays(-(ActivityWindowDays - 1));
        var todayStart = now.Date;

        var totalGoals = await db.Matches.SumAsync(m => (int?)(m.Team1Score + m.Team2Score), cancellationToken) ?? 0;
        var totalMatches = await db.Matches.CountAsync(cancellationToken);

        var totals = new LandingTotalsDto(
            Teams: await db.Teams.CountAsync(cancellationToken),
            Players: await db.Players.CountAsync(p => p.IsActive, cancellationToken),
            Matches: totalMatches,
            MatchesThisWeek: await db.Matches.CountAsync(m => m.PlayedAt >= weekAgo, cancellationToken),
            TotalGoals: totalGoals);

        // Latest results — anonymized: score + time only, never a team identity.
        var recentScores = await db.Matches
            .OrderByDescending(m => m.PlayedAt)
            .Take(RecentScoresLimit)
            .Select(m => new LandingRecentScoreDto(m.Team1Score, m.Team2Score, m.PlayedAt))
            .ToListAsync(cancellationToken);

        // 30-day activity histogram — grouped server-side by day.
        var rawHistogram = await db.Matches
            .Where(m => m.PlayedAt >= windowStart)
            .GroupBy(m => m.PlayedAt.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Day, x => x.Count, cancellationToken);

        var activity = new List<LandingActivityPointDto>(ActivityWindowDays);
        for (var i = 0; i < ActivityWindowDays; i++)
        {
            var day = windowStart.AddDays(i);
            activity.Add(new LandingActivityPointDto(day, rawHistogram.GetValueOrDefault(day, 0)));
        }

        // Fun facts (public-safe — aggregates only, no team or player name).
        var matchesToday = await db.Matches.CountAsync(m => m.PlayedAt >= todayStart, cancellationToken);
        var averageGoalsPerMatch = totalMatches > 0 ? (double)totalGoals / totalMatches : 0;

        // Biggest blowout this week — score margin only, we never surface the team.
        string? biggestBlowout = null;
        var blowoutThisWeek = await db.Matches
            .Where(m => m.PlayedAt >= weekAgo)
            .OrderByDescending(m => Math.Abs(m.Team1Score - m.Team2Score))
            .Select(m => new { m.Team1Score, m.Team2Score })
            .FirstOrDefaultAsync(cancellationToken);

        if (blowoutThisWeek != null)
        {
            var winner = Math.Max(blowoutThisWeek.Team1Score, blowoutThisWeek.Team2Score);
            var loser = Math.Min(blowoutThisWeek.Team1Score, blowoutThisWeek.Team2Score);
            biggestBlowout = $"{winner}–{loser}";
        }

        return new LandingStatsDto(
            totals,
            recentScores,
            activity,
            new LandingFunFactsDto(matchesToday, averageGoalsPerMatch, biggestBlowout));
    }
}
