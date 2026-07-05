using Fotbalek.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Web.Services;

public class LandingStatsService(IDbContextFactory<AppDbContext> dbFactory)
{
    private const int TopTeamsLimit = 5;
    private const int RecentMatchesLimit = 10;
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

        var topTeams = await db.Teams
            .Select(t => new LandingTeamStat
            {
                Name = t.Name,
                CodeName = t.CodeName,
                PlayerCount = t.Players.Count(p => p.IsActive),
                MatchCount = t.Matches.Count()
            })
            .Where(t => t.MatchCount > 0)
            .OrderByDescending(t => t.MatchCount)
            .ThenByDescending(t => t.PlayerCount)
            .Take(TopTeamsLimit)
            .ToListAsync();

        var hottestTeams = await db.Teams
            .Select(t => new LandingTeamStat
            {
                Name = t.Name,
                CodeName = t.CodeName,
                PlayerCount = t.Players.Count(p => p.IsActive),
                MatchCount = t.Matches.Count(m => m.PlayedAt >= weekAgo)
            })
            .Where(t => t.MatchCount > 0)
            .OrderByDescending(t => t.MatchCount)
            .ThenByDescending(t => t.PlayerCount)
            .Take(TopTeamsLimit)
            .ToListAsync();

        // Recent matches (anonymized — only team name + score + time).
        var recentMatches = await db.Matches
            .OrderByDescending(m => m.PlayedAt)
            .Take(RecentMatchesLimit)
            .Select(m => new LandingRecentMatch
            {
                TeamName = m.Team.Name,
                TeamCodeName = m.Team.CodeName,
                Team1Score = m.Team1Score,
                Team2Score = m.Team2Score,
                PlayedAt = m.PlayedAt
            })
            .ToListAsync();

        // 30-day activity histogram — group server-side by day.
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

        // Fun facts (public-safe — no player names).
        var funFacts = new LandingFunFacts
        {
            MatchesToday = await db.Matches.CountAsync(m => m.PlayedAt >= todayStart),
            AverageGoalsPerMatch = totalMatches > 0 ? (double)totalGoals / totalMatches : 0
        };

        var blowoutThisWeek = await db.Matches
            .Where(m => m.PlayedAt >= weekAgo)
            .OrderByDescending(m => Math.Abs(m.Team1Score - m.Team2Score))
            .Select(m => new
            {
                m.Team1Score,
                m.Team2Score,
                TeamName = m.Team.Name
            })
            .FirstOrDefaultAsync();

        if (blowoutThisWeek != null)
        {
            var winner = Math.Max(blowoutThisWeek.Team1Score, blowoutThisWeek.Team2Score);
            var loser = Math.Min(blowoutThisWeek.Team1Score, blowoutThisWeek.Team2Score);
            funFacts.BiggestBlowout = $"{winner}–{loser}";
            funFacts.BiggestBlowoutTeam = blowoutThisWeek.TeamName;
        }

        return new LandingStats
        {
            Totals = totals,
            TopTeams = topTeams,
            HottestTeams = hottestTeams,
            RecentMatches = recentMatches,
            Activity = activity,
            FunFacts = funFacts
        };
    }
}

public class LandingStats
{
    public LandingTotals Totals { get; set; } = new();
    public List<LandingTeamStat> TopTeams { get; set; } = [];
    public List<LandingTeamStat> HottestTeams { get; set; } = [];
    public List<LandingRecentMatch> RecentMatches { get; set; } = [];
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

public class LandingTeamStat
{
    public string Name { get; set; } = string.Empty;
    public string CodeName { get; set; } = string.Empty;
    public int PlayerCount { get; set; }
    public int MatchCount { get; set; }
}

public class LandingRecentMatch
{
    public string TeamName { get; set; } = string.Empty;
    public string TeamCodeName { get; set; } = string.Empty;
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
    public string? BiggestBlowoutTeam { get; set; }
}
