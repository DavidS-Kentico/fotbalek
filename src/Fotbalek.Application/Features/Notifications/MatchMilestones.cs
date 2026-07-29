using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Features.Stats.Core;
using Fotbalek.Application.Features.Stats.Rivalries;
using Fotbalek.Application.Features.Stats.Streaks;
using Fotbalek.Domain.Entities;
using Fotbalek.SharedKernel;

namespace Fotbalek.Application.Features.Notifications;

/// <summary>
/// The four personal milestones, computed from the aftermath's already-loaded team history and
/// always over the ALL-TIME record — a personal best is not scoped to a season
/// (AI/notifications.md §6.5). Only the players in the triggering match are considered.
/// <para>
/// Loss streaks are deliberately absent: the app's humour would support one, but a notification that
/// pings you about losing is the one most likely to make someone turn the feature off (§16).
/// </para>
/// <para>
/// Adds tracked rows only — <b>the caller must SaveChanges</b> (§4.1).
/// </para>
/// </summary>
internal static class MatchMilestones
{
    /// <summary>3, 5, 10, then every 5.</summary>
    public static bool IsWinStreakMilestone(int streak) =>
        Constants.Notifications.WinStreakThresholds.Contains(streak) ||
        (streak > Constants.Notifications.WinStreakThresholds[^1] &&
         streak % Constants.Notifications.WinStreakRepeatEvery == 0);

    /// <summary>10, 25, 50, 100, then every 100.</summary>
    public static bool IsMatchCountMilestone(int matchesPlayed) =>
        Constants.Notifications.MatchMilestones.Contains(matchesPlayed) ||
        (matchesPlayed > Constants.Notifications.MatchMilestones[^1] &&
         matchesPlayed % Constants.Notifications.MatchMilestoneRepeatEvery == 0);

    /// <param name="allTimeMatches">The team's whole history, chronological (PlayedAt then Id), with
    /// MatchPlayers loaded — including <paramref name="match"/> itself.</param>
    public static async Task WriteAsync(
        INotificationWriter writer,
        int teamId,
        Match match,
        IReadOnlyList<Match> allTimeMatches,
        IReadOnlyDictionary<int, Player> playersById,
        CancellationToken cancellationToken)
    {
        var participants = match.MatchPlayers.ToList();
        if (participants.Count == 0)
            return;

        // StreakComputer's own rule (win by score, ordered PlayedAt then Id) rather than a second
        // implementation of it — §6.1's "don't reimplement" applied to the milestones too. The
        // seasonal parts of the context stay unset: this is all-time scope.
        var streaks = StreakComputer.Compute(new StatContext
        {
            Matches = allTimeMatches,
            PlayersById = playersById,
            Ladder = EloLadder.AllTime,
            IsFullScope = true,
        });

        // The nemesis is computed EXCLUDING this match — otherwise the win itself can flip the loss
        // ratio and the notification contradicts its own premise (§6.5).
        var nemeses = NemesisRule.WorstPerPlayer(allTimeMatches.Where(m => m.Id != match.Id));

        var matchesPlayed = new Dictionary<int, int>();
        var peakBefore = new Dictionary<int, int>();
        foreach (var other in allTimeMatches)
        {
            foreach (var mp in other.MatchPlayers)
            {
                matchesPlayed[mp.PlayerId] = matchesPlayed.GetValueOrDefault(mp.PlayerId) + 1;
                if (other.Id == match.Id)
                    continue;
                // PeakEloStat is not callable (its Compute is protected, and its public entry point
                // returns team-wide leaderboard holders rather than a per-player peak), and the rule
                // is one expression anyway: the player's max EloAfter over their EARLIER matches. A
                // deleted match is always the newest one for its players, so "not this match" and
                // "earlier" coincide here.
                if (!peakBefore.TryGetValue(mp.PlayerId, out var best) || mp.EloAfter > best)
                    peakBefore[mp.PlayerId] = mp.EloAfter;
            }
        }

        foreach (var mp in participants)
        {
            if (!playersById.TryGetValue(mp.PlayerId, out var player) || player.UserId is not int userId)
                continue;
            int[] recipients = [userId];

            // Milestone drafts carry NO actor: they are system rows, so a self-recorded match still
            // reaches its own recorder (§4.2).
            if (peakBefore.TryGetValue(mp.PlayerId, out var previousPeak) && mp.EloAfter > previousPeak)
            {
                await writer.AddAsync(
                    new NotificationDraft(NotificationType.PeakElo, teamId, $"peak:{match.Id}")
                    {
                        MatchId = match.Id,
                        Value = mp.EloAfter,
                    },
                    recipients, cancellationToken);
            }

            var winStreak = streaks.GetValueOrDefault(mp.PlayerId)?.CurrentWinStreak ?? 0;
            if (IsWinStreakMilestone(winStreak))
            {
                await writer.AddAsync(
                    new NotificationDraft(NotificationType.WinStreak, teamId, $"streak:{match.Id}")
                    {
                        MatchId = match.Id,
                        Value = winStreak,
                    },
                    recipients, cancellationToken);
            }

            var played = matchesPlayed.GetValueOrDefault(mp.PlayerId);
            if (IsMatchCountMilestone(played))
            {
                await writer.AddAsync(
                    new NotificationDraft(NotificationType.MatchMilestone, teamId, $"milestone:{match.Id}")
                    {
                        MatchId = match.Id,
                        Value = played,
                    },
                    recipients, cancellationToken);
            }

            var won = TeamScore(match, mp.TeamNumber) > TeamScore(match, Opponent(mp.TeamNumber));
            if (won &&
                nemeses.TryGetValue(mp.PlayerId, out var nemesis) &&
                participants.Any(o => o.TeamNumber != mp.TeamNumber && o.PlayerId == nemesis.OpponentPlayerId))
            {
                await writer.AddAsync(
                    new NotificationDraft(
                        NotificationType.NemesisBeaten, teamId,
                        $"nemesis:{match.Id}:{nemesis.OpponentPlayerId}")
                    {
                        MatchId = match.Id,
                        SubjectPlayerId = nemesis.OpponentPlayerId,
                    },
                    recipients, cancellationToken);
            }
        }
    }

    private static int TeamScore(Match match, int teamNumber) =>
        teamNumber == 1 ? match.Team1Score : match.Team2Score;

    private static int Opponent(int teamNumber) => teamNumber == 1 ? 2 : 1;
}
