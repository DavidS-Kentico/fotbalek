using Fotbalek.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Web.Services;

/// <summary>
/// Public landing-page statistics. Everything here is deliberately privacy-safe:
/// only cross-team aggregates and anonymized scores are exposed — never a team's
/// name/code or a player's identity — so a visitor can't learn which specific team
/// is most active or who played whom.
/// </summary>
public class LandingStatsService(IDbContextFactory<AppDbContext> dbFactory)
{
    private const int RecentScoresLimit = 12;
    private const int ActivityWindowDays = 30;

    public async Task<LandingStats> GetAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var now = DateTimeOffset.UtcNow;
        var weekAgo = now.AddDays(-Constants.TimeThresholds.RecentActivityDays);
        var windowStart = now.Date.AddDays(-(ActivityWindowDays - 1));
        var todayStart = now.Date;

        var totalGoals = await db.Matches.SumAsync(m => (int?)(m.Team1Score + m.Team2Score)) ?? 0;
        var totalMatches = await db.Matches.CountAsync();

        var totals = new LandingTotals
        {
            Teams = await db.Teams.CountAsync(),
            Players = await db.Players.CountAsync(p => p.IsActive),
            Matches = totalMatches,
            MatchesThisWeek = await db.Matches.CountAsync(m => m.PlayedAt >= weekAgo),
            TotalGoals = totalGoals
        };

        // Latest results — anonymized: score + time only, never a team identity.
        var recentScores = await db.Matches
            .OrderByDescending(m => m.PlayedAt)
            .Take(RecentScoresLimit)
            .Select(m => new LandingRecentScore
            {
                Team1Score = m.Team1Score,
                Team2Score = m.Team2Score,
                PlayedAt = m.PlayedAt
            })
            .ToListAsync();

        // 30-day activity histogram — grouped server-side by day.
        var rawHistogram = await db.Matches
            .Where(m => m.PlayedAt >= windowStart)
            .GroupBy(m => m.PlayedAt.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Day, x => x.Count);

        var activity = new List<LandingActivityPoint>(ActivityWindowDays);
        for (var i = 0; i < ActivityWindowDays; i++)
        {
            var day = windowStart.AddDays(i);
            activity.Add(new LandingActivityPoint
            {
                Day = day,
                Matches = rawHistogram.GetValueOrDefault(day, 0)
            });
        }

        // Fun facts (public-safe — aggregates only, no team or player name).
        var funFacts = new LandingFunFacts
        {
            MatchesToday = await db.Matches.CountAsync(m => m.PlayedAt >= todayStart),
            AverageGoalsPerMatch = totalMatches > 0 ? (double)totalGoals / totalMatches : 0
        };

        // Biggest blowout this week — score margin only, we never surface the team.
        var blowoutThisWeek = await db.Matches
            .Where(m => m.PlayedAt >= weekAgo)
            .OrderByDescending(m => Math.Abs(m.Team1Score - m.Team2Score))
            .Select(m => new { m.Team1Score, m.Team2Score })
            .FirstOrDefaultAsync();

        if (blowoutThisWeek != null)
        {
            var winner = Math.Max(blowoutThisWeek.Team1Score, blowoutThisWeek.Team2Score);
            var loser = Math.Min(blowoutThisWeek.Team1Score, blowoutThisWeek.Team2Score);
            funFacts.BiggestBlowout = $"{winner}–{loser}";
        }

        return new LandingStats
        {
            Totals = totals,
            RecentScores = recentScores,
            Activity = activity,
            FunFacts = funFacts
        };
    }
}

public class LandingStats
{
    public LandingTotals Totals { get; set; } = new();
    public List<LandingRecentScore> RecentScores { get; set; } = [];
    public List<LandingActivityPoint> Activity { get; set; } = [];
    public LandingFunFacts FunFacts { get; set; } = new();
}

public class LandingTotals
{
    public int Teams { get; set; }
    public int Players { get; set; }
    public int Matches { get; set; }
    public int MatchesThisWeek { get; set; }
    public int TotalGoals { get; set; }
}

/// <summary>A recent match reduced to its score and time — no team identity.</summary>
public class LandingRecentScore
{
    public int Team1Score { get; set; }
    public int Team2Score { get; set; }
    public DateTimeOffset PlayedAt { get; set; }
}

public class LandingActivityPoint
{
    public DateTime Day { get; set; }
    public int Matches { get; set; }
}

public class LandingFunFacts
{
    public int MatchesToday { get; set; }
    public double AverageGoalsPerMatch { get; set; }
    public string? BiggestBlowout { get; set; }
}
