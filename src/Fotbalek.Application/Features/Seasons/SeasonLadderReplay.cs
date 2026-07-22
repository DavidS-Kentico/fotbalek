using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Domain.Entities;
using Fotbalek.Domain.Services;
using Fotbalek.SharedKernel;

namespace Fotbalek.Application.Features.Seasons;

/// <summary>
/// Rebuilds the seasonal ladder from scratch by replaying the given season matches in
/// chronological order (PlayedAt, ties by Id): SeasonPlayer rows created/reset, seasonal ELO
/// computed with the same math as live recording (including the rating floor), and the
/// MatchPlayer.SeasonElo* fields filled. Ladder rows left with zero season matches are deleted.
/// Used by season create (import) and EndsAt shrink.
/// </summary>
internal static class SeasonLadderReplay
{
    public static void Replay(IAppDbContext db, int seasonId, List<SeasonPlayer> existingLadder, List<Match> seasonMatches)
    {
        var ladderByPlayer = existingLadder.ToDictionary(sp => sp.PlayerId);
        foreach (var sp in ladderByPlayer.Values)
        {
            sp.Elo = Constants.Elo.DefaultRating;
        }
        var participated = new HashSet<int>();

        foreach (var match in seasonMatches.OrderBy(m => m.PlayedAt).ThenBy(m => m.Id))
        {
            var team1 = match.MatchPlayers.Where(mp => mp.TeamNumber == 1).ToList();
            var team2 = match.MatchPlayers.Where(mp => mp.TeamNumber == 2).ToList();
            if (team1.Count != 2 || team2.Count != 2) continue;

            var ladder1A = GetOrCreateLadderRow(team1[0].PlayerId);
            var ladder1B = GetOrCreateLadderRow(team1[1].PlayerId);
            var ladder2A = GetOrCreateLadderRow(team2[0].PlayerId);
            var ladder2B = GetOrCreateLadderRow(team2[1].PlayerId);

            var team1Elo = EloCalculator.GetTeamElo(ladder1A.Elo, ladder1B.Elo);
            var team2Elo = EloCalculator.GetTeamElo(ladder2A.Elo, ladder2B.Elo);
            var team1Won = match.Team1Score > match.Team2Score;
            var (change1, change2) = EloCalculator.CalculateEloChange(team1Elo, team2Elo, team1Won);

            Apply(team1[0], ladder1A, change1);
            Apply(team1[1], ladder1B, change1);
            Apply(team2[0], ladder2A, change2);
            Apply(team2[1], ladder2B, change2);
        }

        foreach (var sp in ladderByPlayer.Values)
        {
            if (!participated.Contains(sp.PlayerId))
            {
                db.SeasonPlayers.Remove(sp);
            }
        }

        SeasonPlayer GetOrCreateLadderRow(int playerId)
        {
            if (!ladderByPlayer.TryGetValue(playerId, out var sp))
            {
                sp = new SeasonPlayer { SeasonId = seasonId, PlayerId = playerId, Elo = Constants.Elo.DefaultRating };
                db.SeasonPlayers.Add(sp);
                ladderByPlayer[playerId] = sp;
            }
            participated.Add(playerId);
            return sp;
        }

        static void Apply(MatchPlayer mp, SeasonPlayer sp, int change)
        {
            mp.SeasonEloBefore = sp.Elo;
            sp.Elo = EloCalculator.ApplyEloChange(sp.Elo, change);
            mp.SeasonEloAfter = sp.Elo;
            mp.SeasonEloChange = change;
        }
    }
}
