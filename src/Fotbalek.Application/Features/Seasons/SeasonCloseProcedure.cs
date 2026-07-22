using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Domain.Entities;
using Fotbalek.Domain.Services;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Seasons;

/// <summary>
/// The close procedure: freezes per-player results and pair standings, generates awards
/// (when the season has enough matches), and stamps ClosedAt. Runs inside the caller's
/// transaction, after the season row lock was taken.
/// </summary>
internal static class SeasonCloseProcedure
{
    public static async Task CloseAsync(IAppDbContext db, Season season, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var matches = await db.Matches
            .Include(m => m.MatchPlayers)
            .Where(m => m.SeasonId == season.Id)
            .OrderBy(m => m.PlayedAt).ThenBy(m => m.Id)
            .ToListAsync(cancellationToken);

        // Read-only load — the ladder rows themselves are not touched at close (their Elo is
        // already final), so skip tracking.
        var ladder = await db.SeasonPlayers
            .AsNoTracking()
            .Include(sp => sp.Player)
            .Where(sp => sp.SeasonId == season.Id)
            .ToListAsync(cancellationToken);

        // 1. Freeze results — one SeasonPlayerResult per participant, wins by score.
        var aggregates = SeasonAggregates.ComputeParticipants(matches);

        // FinalRank only for players active at close; deterministic tie-breaks:
        // seasonal ELO desc → wins desc → matches played desc → PlayerId asc.
        var rankByPlayer = ladder
            .Where(sp => sp.Player.IsActive)
            .OrderByDescending(sp => sp.Elo)
            .ThenByDescending(sp => aggregates.TryGetValue(sp.PlayerId, out var a) ? a.Wins : 0)
            .ThenByDescending(sp => aggregates.TryGetValue(sp.PlayerId, out var a) ? a.MatchesPlayed : 0)
            .ThenBy(sp => sp.PlayerId)
            .Select((sp, index) => (sp.PlayerId, Rank: index + 1))
            .ToDictionary(x => x.PlayerId, x => x.Rank);

        var participants = new List<ParticipantClose>();
        foreach (var sp in ladder)
        {
            var agg = aggregates.TryGetValue(sp.PlayerId, out var a) ? a : new SeasonAggregates.ParticipantAggregate();
            var result = new SeasonPlayerResult
            {
                SeasonPlayerId = sp.Id,
                FinalRank = rankByPlayer.TryGetValue(sp.PlayerId, out var rank) ? rank : null,
                Wins = agg.Wins,
                Losses = agg.Losses,
                MatchesPlayed = agg.MatchesPlayed,
                LongestWinStreak = agg.LongestWinStreak,
                LongestLossStreak = agg.LongestLossStreak,
                GoalkeeperMatches = agg.GoalkeeperMatches,
                GoalsConcededAsGoalkeeper = agg.GoalsConcededAsGoalkeeper,
                AttackerMatches = agg.AttackerMatches,
                GoalsScoredAsAttacker = agg.GoalsScoredAsAttacker
            };
            db.SeasonPlayerResults.Add(result);
            participants.Add(new ParticipantClose(sp.PlayerId, sp.Elo, result));
        }

        var pairRows = new List<SeasonPair>();
        foreach (var ((player1Id, player2Id), pair) in SeasonAggregates.ComputePairs(matches))
        {
            var row = new SeasonPair
            {
                SeasonId = season.Id,
                Player1Id = player1Id,
                Player2Id = player2Id,
                MatchesTogether = pair.Matches,
                WinsTogether = pair.Wins
            };
            db.SeasonPairs.Add(row);
            pairRows.Add(row);
        }

        // 2. Awards — only if the season has enough matches in total; standings still freeze below that.
        if (matches.Count >= Constants.Seasons.MinMatchesForAwards)
        {
            GenerateAwards(db, season, participants, pairRows);
        }

        // 3. Close.
        season.EndsAt ??= now;
        season.ClosedAt = now;
    }

    /// <summary>PlayerId + final seasonal ELO + the frozen result row. FinalRank != null ⇔ active at close.</summary>
    private sealed record ParticipantClose(int PlayerId, int Elo, SeasonPlayerResult Result);

    private static void GenerateAwards(IAppDbContext db, Season season, List<ParticipantClose> participants, List<SeasonPair> pairs)
    {
        var byPlayer = participants.ToDictionary(p => p.PlayerId);

        // Top 3 players: the frozen standings order filtered to the Player-award match minimum —
        // the award champion and the standings leader can therefore disagree.
        var playerPodium = participants
            .Where(p => p.Result.FinalRank != null && p.Result.MatchesPlayed >= Constants.Seasons.MinMatchesForPlayerAward)
            .OrderBy(p => p.Result.FinalRank)
            .Take(3)
            .ToList();
        AddAwards(Constants.Seasons.AwardCategories.Player, playerPodium.Select(p => p.PlayerId));

        // Top 3 goalkeepers: fewest goals conceded per game; same threshold and metric as the rankings.
        var goalkeeperPodium = participants
            .Where(p => p.Result.FinalRank != null && p.Result.GoalkeeperMatches >= Constants.TimeThresholds.MinGamesForPositionBadge)
            .OrderBy(p => (double)p.Result.GoalsConcededAsGoalkeeper / p.Result.GoalkeeperMatches)
            .ThenByDescending(p => p.Result.GoalkeeperMatches)
            .ThenByDescending(p => p.Elo)
            .ThenBy(p => p.PlayerId)
            .Take(3)
            .ToList();
        AddAwards(Constants.Seasons.AwardCategories.Goalkeeper, goalkeeperPodium.Select(p => p.PlayerId));

        // Top 3 attackers: most goals scored per game.
        var attackerPodium = participants
            .Where(p => p.Result.FinalRank != null && p.Result.AttackerMatches >= Constants.TimeThresholds.MinGamesForPositionBadge)
            .OrderByDescending(p => (double)p.Result.GoalsScoredAsAttacker / p.Result.AttackerMatches)
            .ThenByDescending(p => p.Result.AttackerMatches)
            .ThenByDescending(p => p.Elo)
            .ThenBy(p => p.PlayerId)
            .Take(3)
            .ToList();
        AddAwards(Constants.Seasons.AwardCategories.Attacker, attackerPodium.Select(p => p.PlayerId));

        // Top 3 pairs: win rate together; excluded if either member is inactive at close.
        var pairPodium = pairs
            .Where(pr => pr.MatchesTogether >= Constants.TimeThresholds.MinGamesForPartnerStats &&
                         byPlayer.TryGetValue(pr.Player1Id, out var m1) && m1.Result.FinalRank != null &&
                         byPlayer.TryGetValue(pr.Player2Id, out var m2) && m2.Result.FinalRank != null)
            .OrderByDescending(pr => (double)pr.WinsTogether / pr.MatchesTogether)
            .ThenByDescending(pr => pr.MatchesTogether)
            .ThenByDescending(pr => byPlayer[pr.Player1Id].Elo + byPlayer[pr.Player2Id].Elo)
            .ThenBy(pr => Math.Min(pr.Player1Id, pr.Player2Id))
            .Take(3)
            .ToList();

        var pairRank = 1;
        foreach (var pair in pairPodium)
        {
            // One row per member so lookups by PlayerId stay trivial.
            db.SeasonAwards.Add(new SeasonAward
            {
                SeasonId = season.Id,
                PlayerId = pair.Player1Id,
                PartnerPlayerId = pair.Player2Id,
                Category = Constants.Seasons.AwardCategories.Pair,
                Rank = pairRank
            });
            db.SeasonAwards.Add(new SeasonAward
            {
                SeasonId = season.Id,
                PlayerId = pair.Player2Id,
                PartnerPlayerId = pair.Player1Id,
                Category = Constants.Seasons.AwardCategories.Pair,
                Rank = pairRank
            });
            pairRank++;
        }

        void AddAwards(string category, IEnumerable<int> playerIdsInOrder)
        {
            var rank = 1;
            foreach (var playerId in playerIdsInOrder)
            {
                db.SeasonAwards.Add(new SeasonAward
                {
                    SeasonId = season.Id,
                    PlayerId = playerId,
                    Category = category,
                    Rank = rank++
                });
            }
        }
    }
}
