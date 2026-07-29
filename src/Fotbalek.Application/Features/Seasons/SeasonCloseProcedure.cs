using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Features.Stats.Queries;
using Fotbalek.Domain.Entities;
using Fotbalek.Domain.Services;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Seasons;

/// <summary>What the close froze, handed back to the caller so the notification write has a typed
/// input instead of having to walk <c>ChangeTracker.Entries&lt;T&gt;()</c> for Added rows — which would
/// be both obscure and fragile against a future reordering (AI/notifications.md §5.5).</summary>
/// <param name="Ranks">One entry per participant. A null rank means inactive at close.</param>
internal sealed record SeasonCloseResult(
    IReadOnlyList<(int PlayerId, int? FinalRank)> Ranks,
    IReadOnlyList<SeasonAward> Awards);

/// <summary>
/// The close procedure: freezes per-player results and pair standings, generates awards
/// (when the season has enough matches), and stamps ClosedAt. Runs inside the caller's
/// transaction, after the season row lock was taken.
/// <para>
/// Has TWO callers — CloseSeasonCommand (the lazy close) and EndSeasonNowCommand (a captain ending
/// the season early) — and both write the season-close notifications from what this returns. Hooking
/// only the first would make an early-ended season finish in silence, for exactly the close a human
/// deliberately triggered (§5.5).
/// </para>
/// </summary>
internal static class SeasonCloseProcedure
{
    public static async Task<SeasonCloseResult> CloseAsync(IAppDbContext db, Season season, DateTimeOffset now, CancellationToken cancellationToken)
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

        // FinalRank only for players active at close, ranked by the shared solo chain — the frozen
        // standings and the live table order through the same rules and can never disagree.
        var rankByPlayer = ladder
            .Where(sp => sp.Player.IsActive)
            .OrderSolo(sp =>
            {
                var agg = aggregates.GetValueOrDefault(sp.PlayerId);
                return new LadderLeaders.SoloKey(sp.Elo, agg?.Wins ?? 0, agg?.MatchesPlayed ?? 0, sp.PlayerId);
            })
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

        // 2. Awards — only if the season has enough matches in total; standings still freeze below
        // that, so a short season closes with standings and no awards at all. The notification write
        // simply iterates whatever came out here, which may be nothing.
        var awards = matches.Count >= Constants.Seasons.MinMatchesForAwards
            ? GenerateAwards(db, season, participants, pairRows)
            : [];

        // 3. Close.
        season.EndsAt ??= now;
        season.ClosedAt = now;

        return new SeasonCloseResult(
            participants.Select(p => (p.PlayerId, p.Result.FinalRank)).ToList(),
            awards);
    }

    /// <summary>PlayerId + final seasonal ELO + the frozen result row. FinalRank != null ⇔ active at close.</summary>
    private sealed record ParticipantClose(int PlayerId, int Elo, SeasonPlayerResult Result);

    private static List<SeasonAward> GenerateAwards(IAppDbContext db, Season season, List<ParticipantClose> participants, List<SeasonPair> pairs)
    {
        var awards = new List<SeasonAward>();
        var byPlayer = participants.ToDictionary(p => p.PlayerId);

        // Top 3 players: the frozen standings order filtered to the Player-award match minimum —
        // the award champion and the standings leader can therefore disagree.
        var playerPodium = participants
            .Where(p => p.Result.FinalRank != null && p.Result.MatchesPlayed >= Constants.Seasons.MinMatchesForPlayerAward)
            .OrderBy(p => p.Result.FinalRank)
            .Take(3)
            .ToList();
        AddAwards(Constants.Seasons.AwardCategories.Player, playerPodium.Select(p => p.PlayerId));

        // Top 3 goalkeepers: fewest goals conceded per game — the shared chain and threshold, so the
        // podium always matches the rankings table.
        var goalkeeperPodium = participants
            .Where(p => p.Result.FinalRank != null && LadderLeaders.IsPositionEligible(p.Result.GoalkeeperMatches))
            .OrderGoalkeepers(p => new LadderLeaders.PositionKey(
                (double)p.Result.GoalsConcededAsGoalkeeper / p.Result.GoalkeeperMatches,
                p.Result.GoalkeeperMatches, p.Elo, p.PlayerId))
            .Take(3)
            .ToList();
        AddAwards(Constants.Seasons.AwardCategories.Goalkeeper, goalkeeperPodium.Select(p => p.PlayerId));

        // Top 3 attackers: most goals scored per game.
        var attackerPodium = participants
            .Where(p => p.Result.FinalRank != null && LadderLeaders.IsPositionEligible(p.Result.AttackerMatches))
            .OrderAttackers(p => new LadderLeaders.PositionKey(
                (double)p.Result.GoalsScoredAsAttacker / p.Result.AttackerMatches,
                p.Result.AttackerMatches, p.Elo, p.PlayerId))
            .Take(3)
            .ToList();
        AddAwards(Constants.Seasons.AwardCategories.Attacker, attackerPodium.Select(p => p.PlayerId));

        // Top 3 pairs: win rate together; excluded if either member is inactive at close.
        var pairPodium = pairs
            .Where(pr => LadderLeaders.IsPairEligible(pr.MatchesTogether) &&
                         byPlayer.TryGetValue(pr.Player1Id, out var m1) && m1.Result.FinalRank != null &&
                         byPlayer.TryGetValue(pr.Player2Id, out var m2) && m2.Result.FinalRank != null)
            .OrderPairs(pr => new LadderLeaders.PairKey(
                (double)pr.WinsTogether / pr.MatchesTogether,
                pr.MatchesTogether,
                byPlayer[pr.Player1Id].Elo + byPlayer[pr.Player2Id].Elo,
                Math.Min(pr.Player1Id, pr.Player2Id)))
            .Take(3)
            .ToList();

        var pairRank = 1;
        foreach (var pair in pairPodium)
        {
            // One row per member so lookups by PlayerId stay trivial.
            Add(new SeasonAward
            {
                SeasonId = season.Id,
                PlayerId = pair.Player1Id,
                PartnerPlayerId = pair.Player2Id,
                Category = Constants.Seasons.AwardCategories.Pair,
                Rank = pairRank
            });
            Add(new SeasonAward
            {
                SeasonId = season.Id,
                PlayerId = pair.Player2Id,
                PartnerPlayerId = pair.Player1Id,
                Category = Constants.Seasons.AwardCategories.Pair,
                Rank = pairRank
            });
            pairRank++;
        }

        return awards;

        void AddAwards(string category, IEnumerable<int> playerIdsInOrder)
        {
            var rank = 1;
            foreach (var playerId in playerIdsInOrder)
            {
                Add(new SeasonAward
                {
                    SeasonId = season.Id,
                    PlayerId = playerId,
                    Category = category,
                    Rank = rank++
                });
            }
        }

        void Add(SeasonAward award)
        {
            db.SeasonAwards.Add(award);
            awards.Add(award);
        }
    }
}
