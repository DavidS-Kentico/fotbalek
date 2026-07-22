using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Application.Features.Stats.Activity;
using Fotbalek.Application.Features.Stats.Core;
using Fotbalek.Application.Features.Stats.Special;
using Fotbalek.Contracts.Stats;
using Fotbalek.Domain.Entities;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Stats.Queries;

/// <summary>
/// Per-player aggregates, parameterized by (match subset, ladder): with a <paramref name="SeasonId"/>
/// only that season's matches count and every ELO-based figure reads the SeasonElo* fields;
/// otherwise all matches and the all-time ladder. Wins are determined by score in both scopes.
/// </summary>
public sealed record GetPlayerStatsQuery(int PlayerId, int? SeasonId = null) : IQuery<PlayerStats>;

internal sealed class GetPlayerStatsQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<GetPlayerStatsQuery, PlayerStats>
{
    public async Task<Result<PlayerStats>> Handle(GetPlayerStatsQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberOfPlayerTeamAsync(query.PlayerId, cancellationToken))
            return Result.Failure<PlayerStats>(CommonErrors.NotMember);

        var playerId = query.PlayerId;
        var seasonId = query.SeasonId;

        // Tracking query on purpose: the include path (MatchPlayer→Match→MatchPlayers) is a
        // cycle, which EF forbids in no-tracking queries. The context is scoped per dispatch,
        // so the tracked graph dies with the scope.
        var matchPlayers = await db.MatchPlayers
            .Where(mp => mp.PlayerId == playerId && (seasonId == null || mp.Match.SeasonId == seasonId))
            .Include(mp => mp.Match)
                .ThenInclude(m => m.MatchPlayers)
                    .ThenInclude(mp => mp.Player)
            .OrderBy(mp => mp.Match.PlayedAt)
            .ToListAsync(cancellationToken);

        var player = await db.Players.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == playerId, cancellationToken);
        if (player == null)
            return new PlayerStats();

        // Ladder accessors: seasonal fields in season scope, classic fields otherwise.
        var seasonScope = seasonId != null;
        int EloBeforeOf(MatchPlayer mp) => seasonScope ? mp.SeasonEloBefore ?? Constants.Elo.DefaultRating : mp.EloBefore;
        int EloAfterOf(MatchPlayer mp) => seasonScope ? mp.SeasonEloAfter ?? Constants.Elo.DefaultRating : mp.EloAfter;
        int EloChangeOf(MatchPlayer mp) => seasonScope ? mp.SeasonEloChange ?? 0 : mp.EloChange;

        var currentElo = player.Elo;
        if (seasonScope)
        {
            // Seasonal ELO from the ladder row; default 1000 when the player has no seasonal match.
            currentElo = await db.SeasonPlayers
                .Where(sp => sp.SeasonId == seasonId && sp.PlayerId == playerId)
                .Select(sp => (int?)sp.Elo)
                .FirstOrDefaultAsync(cancellationToken) ?? Constants.Elo.DefaultRating;
        }

        var stats = new PlayerStats
        {
            CurrentElo = currentElo,
            TotalMatches = matchPlayers.Count
        };

        if (matchPlayers.Count == 0)
        {
            stats.HighestElo = currentElo;
            stats.LowestElo = currentElo;
            return stats;
        }

        // ELO history - consider all ELO values including current
        var allEloValues = matchPlayers.Select(EloBeforeOf)
            .Concat(matchPlayers.Select(EloAfterOf))
            .Append(currentElo);
        stats.HighestElo = allEloValues.Max();
        stats.LowestElo = allEloValues.Min();

        // Calculate streaks and position stats using helper
        var streakResult = StatsCalculations.CalculateStreaksAndPositionStats(matchPlayers);

        stats.Wins = streakResult.Wins;
        stats.Losses = streakResult.Losses;
        stats.CurrentStreak = streakResult.CurrentStreak;
        stats.LongestWinStreak = streakResult.LongestWinStreak;
        stats.LongestLossStreak = streakResult.LongestLossStreak;
        stats.UnderTableCount = streakResult.UnderTableCount;
        stats.TableSenderCount = streakResult.TableSenderCount;
        stats.GamesAsGk = streakResult.GoalkeeperCount;
        stats.GamesAsAtk = streakResult.AttackerCount;
        stats.WinsAsGk = streakResult.WinsAsGk;
        stats.WinsAsAtk = streakResult.WinsAsAtk;
        stats.WinRateAsGk = streakResult.GoalkeeperCount > 0
            ? (double)streakResult.WinsAsGk / streakResult.GoalkeeperCount * 100
            : 0;
        stats.WinRateAsAtk = streakResult.AttackerCount > 0
            ? (double)streakResult.WinsAsAtk / streakResult.AttackerCount * 100
            : 0;
        stats.GoalsScoredAsGk = streakResult.GoalsScoredAsGk;
        stats.GoalsConcededAsGk = streakResult.GoalsConcededAsGk;
        stats.GoalsScoredAsAtk = streakResult.GoalsScoredAsAtk;
        stats.GoalsConcededAsAtk = streakResult.GoalsConcededAsAtk;

        stats.WinRate = stats.TotalMatches > 0 ? (double)stats.Wins / stats.TotalMatches * 100 : 0;

        // Preferred position
        var totalPositions = streakResult.GoalkeeperCount + streakResult.AttackerCount;
        if (totalPositions > 0)
        {
            var gkRatio = (double)streakResult.GoalkeeperCount / totalPositions;
            if (gkRatio > 0.6)
                stats.PreferredPosition = Constants.Positions.Goalkeeper;
            else if (gkRatio < 0.4)
                stats.PreferredPosition = Constants.Positions.Attacker;
            else
                stats.PreferredPosition = "Flexible";
        }

        // Better position - the role with the higher win rate (min 3 games per position to compare).
        var minGames = Constants.TimeThresholds.MinGamesForPartnerStats;
        var hasGkData = streakResult.GoalkeeperCount >= minGames;
        var hasAtkData = streakResult.AttackerCount >= minGames;

        if (hasGkData && hasAtkData)
        {
            var diff = stats.WinRateAsGk - stats.WinRateAsAtk;
            if (Math.Abs(diff) < 5)
                stats.BetterPosition = "Either";
            else
                stats.BetterPosition = diff > 0 ? Constants.Positions.Goalkeeper : Constants.Positions.Attacker;
        }
        else if (hasGkData)
        {
            stats.BetterPosition = Constants.Positions.Goalkeeper;
        }
        else if (hasAtkData)
        {
            stats.BetterPosition = Constants.Positions.Attacker;
        }

        // Partner stats (full list; min-games filtering happens at display time)
        var partnerStats = matchPlayers
            .SelectMany(mp => mp.Match.MatchPlayers
                .Where(p => p.TeamNumber == mp.TeamNumber && p.PlayerId != playerId)
                .Select(p => new { Partner = p, Won = mp.IsWinner(), OwnEloChange = EloChangeOf(mp) }))
            .GroupBy(x => x.Partner.PlayerId)
            .Select(g => new RelationshipStat
            {
                PlayerId = g.Key,
                Name = g.First().Partner.Player.Name,
                AvatarId = g.First().Partner.Player.AvatarId,
                Games = g.Count(),
                Wins = g.Count(x => x.Won),
                AvgEloChange = g.Average(x => (double)x.OwnEloChange)
            })
            .OrderByDescending(p => p.Games)
            .ToList();

        foreach (var p in partnerStats) p.WinRate = p.Games > 0 ? (double)p.Wins / p.Games * 100 : 0;
        stats.Partners = partnerStats;

        var qualifyingPartners = partnerStats
            .Where(p => p.Games >= Constants.TimeThresholds.MinGamesForPartnerStats)
            .ToList();
        if (qualifyingPartners.Count != 0)
        {
            var best = qualifyingPartners.OrderByDescending(p => p.WinRate).First();
            stats.BestPartner = best.Name;
            stats.BestPartnerWinRate = best.WinRate;
            stats.BestPartnerGames = best.Games;

            if (qualifyingPartners.Count > 1)
            {
                var worst = qualifyingPartners.OrderBy(p => p.WinRate).First();
                stats.WorstPartner = worst.Name;
                stats.WorstPartnerWinRate = worst.WinRate;
                stats.WorstPartnerGames = worst.Games;
            }
        }

        // Enemy stats (full list; win counted from the player's perspective, by score)
        var enemyStats = matchPlayers
            .SelectMany(mp => mp.Match.MatchPlayers
                .Where(p => p.TeamNumber != mp.TeamNumber)
                .Select(p => new { Enemy = p, Won = mp.IsWinner(), OwnEloChange = EloChangeOf(mp) }))
            .GroupBy(x => x.Enemy.PlayerId)
            .Select(g => new RelationshipStat
            {
                PlayerId = g.Key,
                Name = g.First().Enemy.Player.Name,
                AvatarId = g.First().Enemy.Player.AvatarId,
                Games = g.Count(),
                Wins = g.Count(x => x.Won),
                AvgEloChange = g.Average(x => (double)x.OwnEloChange)
            })
            .OrderByDescending(e => e.Games)
            .ToList();

        foreach (var e in enemyStats) e.WinRate = e.Games > 0 ? (double)e.Wins / e.Games * 100 : 0;
        stats.Enemies = enemyStats;

        var qualifyingEnemies = enemyStats
            .Where(e => e.Games >= Constants.TimeThresholds.MinGamesForPartnerStats)
            .ToList();
        if (qualifyingEnemies.Count != 0)
        {
            var easiest = qualifyingEnemies.OrderByDescending(e => e.WinRate).First();
            stats.EasiestEnemy = easiest.Name;
            stats.EasiestEnemyWinRate = easiest.WinRate;
            stats.EasiestEnemyGames = easiest.Games;

            if (qualifyingEnemies.Count > 1)
            {
                var hardest = qualifyingEnemies.OrderBy(e => e.WinRate).First();
                stats.HardestEnemy = hardest.Name;
                stats.HardestEnemyWinRate = hardest.WinRate;
                stats.HardestEnemyGames = hardest.Games;
            }
        }

        // Average ELO change on wins / losses (win/loss by score, change from the selected ladder)
        var winningMps = matchPlayers.Where(mp => mp.IsWinner()).ToList();
        var losingMps = matchPlayers.Where(mp => !mp.IsWinner()).ToList();
        stats.AvgEloChangeOnWin = winningMps.Count > 0 ? winningMps.Average(EloChangeOf) : 0;
        stats.AvgEloChangeOnLoss = losingMps.Count > 0 ? losingMps.Average(EloChangeOf) : 0;

        // Average opponent / teammate ELO (using pre-match ELO of the selected ladder)
        var opponentElos = matchPlayers
            .SelectMany(mp => mp.Match.MatchPlayers
                .Where(p => p.TeamNumber != mp.TeamNumber)
                .Select(EloBeforeOf))
            .ToList();
        stats.AvgOpponentElo = opponentElos.Count > 0 ? (int)Math.Round(opponentElos.Average()) : 0;

        var teammateElos = matchPlayers
            .SelectMany(mp => mp.Match.MatchPlayers
                .Where(p => p.TeamNumber == mp.TeamNumber && p.PlayerId != playerId)
                .Select(EloBeforeOf))
            .ToList();
        stats.AvgTeammateElo = teammateElos.Count > 0 ? (int)Math.Round(teammateElos.Average()) : 0;

        // Recent form: last 10 matches (matchPlayers is ordered ascending by PlayedAt)
        var recent = matchPlayers.TakeLast(10).ToList();
        stats.RecentForm = recent.Select(mp => mp.IsWinner()).ToList();
        stats.RecentFormWinRate = recent.Count > 0
            ? (double)stats.RecentForm.Count(w => w) / recent.Count * 100
            : 0;

        // Teammate variety — Pielou's evenness against the current active roster.
        // Single source of truth: VarietyPlayerStat.ComputeEvenness.
        var activeTeammateIds = await db.Players
            .Where(p => p.TeamId == player.TeamId && p.IsActive && p.Id != playerId)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        var activeTeammateIdSet = activeTeammateIds.ToHashSet();
        var gamesPerActivePartner = matchPlayers
            .SelectMany(mp => mp.Match.MatchPlayers
                .Where(p => p.TeamNumber == mp.TeamNumber && p.PlayerId != playerId))
            .Where(p => activeTeammateIdSet.Contains(p.PlayerId))
            .GroupBy(p => p.PlayerId)
            .ToDictionary(g => g.Key, g => g.Count());

        stats.UniqueTeammates = gamesPerActivePartner.Count;
        stats.ActiveRosterPartners = activeTeammateIds.Count;
        var teammateGames = gamesPerActivePartner.Values.Sum();
        stats.TeammateVariety = teammateGames >= Constants.TimeThresholds.MinGamesForVarietyBadge
            ? VarietyPlayerStat.ComputeEvenness(gamesPerActivePartner, activeTeammateIds.Count)
            : 0;
        stats.HasEnoughGamesForVariety = teammateGames >= Constants.TimeThresholds.MinGamesForVarietyBadge;

        // Carry / Carried counts — uses the same predicate as CarriedStat (single source of truth).
        foreach (var mp in matchPlayers)
        {
            if (!mp.Match.TryGetTeams(out var winners, out var losers)) continue;
            var carry = CarriedStat.AnalyzeCarry(winners[0], winners[1], losers[0], losers[1], EloBeforeOf);
            if (carry is null) continue;
            if (carry.Value.CarriedId == playerId) stats.CarriedCount++;
            if (carry.Value.CarrierId == playerId) stats.CarryCount++;
        }

        // ELO history for chart
        stats.EloHistory = matchPlayers
            .Select(mp => new EloHistoryPoint
            {
                Date = mp.Match.PlayedAt,
                Elo = EloAfterOf(mp)
            })
            .ToList();

        // Per-match derived: expected wins, opponent-strength buckets, goal margins, clean sheets,
        // day-of-week activity.
        const int StrongerThreshold = 50; // ELO points
        double expectedWins = 0;
        int gamesVsStronger = 0, winsVsStronger = 0;
        int gamesVsWeaker = 0, winsVsWeaker = 0;
        double winMarginSum = 0; int winMarginCount = 0;
        double lossMarginSum = 0; int lossMarginCount = 0;
        int cleanSheetsAsGk = 0;
        var dowGames = new int[7];
        var dowWins = new int[7];

        foreach (var mp in matchPlayers)
        {
            var opp = mp.Match.MatchPlayers.Where(p => p.TeamNumber != mp.TeamNumber).ToList();
            if (opp.Count == 0) continue;

            var teammate = mp.Match.MatchPlayers
                .FirstOrDefault(p => p.TeamNumber == mp.TeamNumber && p.PlayerId != playerId);
            var teamAvg = teammate != null ? (EloBeforeOf(mp) + EloBeforeOf(teammate)) / 2.0 : EloBeforeOf(mp);
            var oppAvg = opp.Average(p => (double)EloBeforeOf(p));
            var expected = 1.0 / (1.0 + Math.Pow(10, (oppAvg - teamAvg) / 400.0));
            expectedWins += expected;

            var won = mp.IsWinner();
            if (oppAvg - EloBeforeOf(mp) >= StrongerThreshold)
            {
                gamesVsStronger++;
                if (won) winsVsStronger++;
            }
            else if (EloBeforeOf(mp) - oppAvg >= StrongerThreshold)
            {
                gamesVsWeaker++;
                if (won) winsVsWeaker++;
            }

            var ownScore = mp.TeamNumber == 1 ? mp.Match.Team1Score : mp.Match.Team2Score;
            var oppScore = mp.TeamNumber == 1 ? mp.Match.Team2Score : mp.Match.Team1Score;
            var margin = ownScore - oppScore;
            if (won) { winMarginSum += margin; winMarginCount++; }
            else { lossMarginSum += -margin; lossMarginCount++; }

            if (mp.Position == Constants.Positions.Goalkeeper && oppScore == 0)
                cleanSheetsAsGk++;

            var dow = (int)mp.Match.PlayedAt.DayOfWeek;
            dowGames[dow]++;
            if (won) dowWins[dow]++;
        }

        stats.ExpectedWins = expectedWins;
        stats.GamesVsStronger = gamesVsStronger;
        stats.WinsVsStronger = winsVsStronger;
        stats.WinRateVsStronger = gamesVsStronger > 0 ? (double)winsVsStronger / gamesVsStronger * 100 : 0;
        stats.GamesVsWeaker = gamesVsWeaker;
        stats.WinsVsWeaker = winsVsWeaker;
        stats.WinRateVsWeaker = gamesVsWeaker > 0 ? (double)winsVsWeaker / gamesVsWeaker * 100 : 0;
        stats.AvgWinMargin = winMarginCount > 0 ? winMarginSum / winMarginCount : 0;
        stats.AvgLossMargin = lossMarginCount > 0 ? lossMarginSum / lossMarginCount : 0;
        stats.CleanSheetsAsGk = cleanSheetsAsGk;
        stats.MatchesByDayOfWeek = Enumerable.Range(0, 7)
            .Select(d => new DayOfWeekStat((DayOfWeek)d, dowGames[d], dowWins[d]))
            .ToList();

        // Matches per month (last 12 months, oldest first)
        var nowUtc = DateTimeOffset.UtcNow;
        var months = new List<ActivityMonth>(12);
        for (int i = 11; i >= 0; i--)
        {
            var d = nowUtc.AddMonths(-i);
            var games = matchPlayers.Count(mp =>
                mp.Match.PlayedAt.Year == d.Year && mp.Match.PlayedAt.Month == d.Month);
            months.Add(new ActivityMonth(d.Year, d.Month, games));
        }
        stats.MatchesByMonth = months;

        // Milestones
        stats.FirstMatchDate = matchPlayers.FirstOrDefault()?.Match.PlayedAt;
        if (matchPlayers.Count > 0)
        {
            var biggestGain = matchPlayers.OrderByDescending(EloChangeOf).First();
            stats.BiggestEloGain = EloChangeOf(biggestGain);
            stats.BiggestEloGainDate = biggestGain.Match.PlayedAt;

            var biggestLoss = matchPlayers.OrderBy(EloChangeOf).First();
            stats.BiggestEloLoss = EloChangeOf(biggestLoss);
            stats.BiggestEloLossDate = biggestLoss.Match.PlayedAt;

            var peak = matchPlayers.OrderByDescending(EloAfterOf).First();
            stats.PeakEloDate = peak.Match.PlayedAt;
        }

        return stats;
    }
}
