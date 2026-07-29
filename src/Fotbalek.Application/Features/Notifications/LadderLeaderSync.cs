using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Features.Stats.Queries;
using Fotbalek.Domain.Entities;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Notifications;

/// <summary>
/// Compares the four computed #1s of one scope against the persisted <see cref="LadderLeader"/>
/// snapshot and reconciles the difference (AI/notifications.md §6.3).
/// <para>
/// <b>The invariant this exists to keep:</b> a snapshot row is never allowed to be out of date,
/// whether or not anyone was told. That is why every evaluation refreshes BOTH scopes while only one
/// announces — a seasonal match also moves all-time ELO (the seasonal pass is additional, not
/// alternative), so refreshing only the season's ladders would leave the all-time snapshot saying
/// whatever it said weeks ago, and the next off-season match would announce a lead change that
/// happened long ago or never happened at all (§6.2).
/// </para>
/// <para>
/// Adds and mutates tracked entities only — <b>the caller must SaveChanges</b> (§4.1).
/// </para>
/// </summary>
internal sealed class LadderLeaderSync(IAppDbContext db, INotificationWriter writer)
{
    /// <summary>Refreshes the all-time set. Everyone on the roster carries an all-time rating, so
    /// <c>Player.Elo</c> is the ladder rating directly.</summary>
    /// <param name="announceMatchId">The match whose changes may be announced — null keeps the
    /// refresh silent (§6.2). It doubles as the dedup-key anchor, which is why "announce without a
    /// match" is not representable here.</param>
    public Task SyncAllTimeAsync(
        int teamId,
        int? announceMatchId,
        IReadOnlyList<Match> matches,
        IReadOnlyDictionary<int, Player> playersById,
        CancellationToken cancellationToken) =>
        SyncAsync(
            teamId,
            seasonId: null,
            announceMatchId,
            matches,
            playersById,
            playerId => playersById.TryGetValue(playerId, out var player) ? player.Elo : null,
            cancellationToken);

    /// <summary>Refreshes one season's set from the already-loaded team history: filters the matches
    /// to the season and loads that season's ratings — the seasonal solo ladder ranks on
    /// <c>SeasonPlayer.Elo</c>, which no amount of match data substitutes for. A roster player with
    /// no seasonal row gets null, so they never default into the ladder at 1000.</summary>
    /// <param name="announceMatchId">See <see cref="SyncAllTimeAsync"/>.</param>
    public async Task SyncSeasonAsync(
        int teamId,
        int seasonId,
        int? announceMatchId,
        IReadOnlyList<Match> matches,
        IReadOnlyDictionary<int, Player> playersById,
        CancellationToken cancellationToken)
    {
        var seasonElo = await db.SeasonPlayers
            .AsNoTracking()
            .Where(sp => sp.SeasonId == seasonId)
            .ToDictionaryAsync(sp => sp.PlayerId, sp => sp.Elo, cancellationToken);

        await SyncAsync(
            teamId,
            seasonId,
            announceMatchId,
            matches.Where(m => m.SeasonId == seasonId).ToList(),
            playersById,
            playerId => seasonElo.TryGetValue(playerId, out var elo) ? elo : null,
            cancellationToken);
    }

    /// <param name="scopeMatches">The scope's matches, chronological, MatchPlayers loaded.</param>
    /// <param name="playersById">The team's players — the source of IsActive.</param>
    /// <param name="eloOf">The scope's rating, or null when the player is not on this ladder.</param>
    private async Task SyncAsync(
        int teamId,
        int? seasonId,
        int? announceMatchId,
        IReadOnlyList<Match> scopeMatches,
        IReadOnlyDictionary<int, Player> playersById,
        Func<int, int?> eloOf,
        CancellationToken cancellationToken)
    {
        var computed = LadderLeaders.Compute(scopeMatches, playersById, eloOf);

        // Tracked on purpose: the reconciliation below mutates and removes these rows. The season
        // filter is built in C# rather than as `l.SeasonId == seasonId`, so the all-time scope
        // asks for IS NULL instead of relying on the provider's null semantics.
        var snapshotQuery = db.LadderLeaders.Where(l => l.TeamId == teamId);
        snapshotQuery = seasonId is int scopeSeasonId
            ? snapshotQuery.Where(l => l.SeasonId == scopeSeasonId)
            : snapshotQuery.Where(l => l.SeasonId == null);
        var snapshots = await snapshotQuery.ToListAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;

        foreach (var category in LadderLeaders.Categories)
        {
            var snapshot = snapshots.FirstOrDefault(l => l.Category == category);
            computed.TryGetValue(category, out var top);

            if (top == null)
            {
                // The ladder became empty (the last eligible player was deactivated) — drop the row
                // so the next leader is a first evaluation again, and tell nobody.
                if (snapshot != null)
                    db.LadderLeaders.Remove(snapshot);
                continue;
            }

            if (snapshot == null)
            {
                // FIRST evaluation of this (team, scope, category): write the snapshot SILENTLY. This
                // is what stands in for a backfill — the feature must not announce four leads that
                // have been true for months, and after a single match in a new season "you're #1" is
                // noise, not news (§3.4, §3.6).
                db.LadderLeaders.Add(new LadderLeader
                {
                    TeamId = teamId,
                    SeasonId = seasonId,
                    Category = category,
                    PlayerId = top.PlayerId,
                    PartnerPlayerId = top.PartnerPlayerId,
                    EvaluatedAt = now,
                });
                continue;
            }

            if (snapshot.PlayerId == top.PlayerId && snapshot.PartnerPlayerId == top.PartnerPlayerId)
                continue;

            var previousPlayerId = snapshot.PlayerId;
            var previousPartnerId = snapshot.PartnerPlayerId;

            snapshot.PlayerId = top.PlayerId;
            snapshot.PartnerPlayerId = top.PartnerPlayerId;
            snapshot.EvaluatedAt = now;

            if (announceMatchId is not int matchId)
                continue;

            await AnnounceAsync(
                teamId, seasonId, category, matchId, top, previousPlayerId, previousPartnerId, cancellationToken);
        }
    }

    private async Task AnnounceAsync(
        int teamId,
        int? seasonId,
        string category,
        int matchId,
        LadderLeaders.LadderTop top,
        int previousPlayerId,
        int? previousPartnerId,
        CancellationToken cancellationToken)
    {
        var scopeKey = seasonId?.ToString() ?? "all";

        // A pair change notifies both members of BOTH pairs even when one member is unchanged — the
        // duo is the unit (§6.3). Each new-leader row names the OTHER member as its subject, so the
        // members need one write each rather than a shared one.
        foreach (var (memberPlayerId, partnerPlayerId) in Members(top.PlayerId, top.PartnerPlayerId))
        {
            var recipients = await NotificationRecipients.ForPlayersAsync(
                db, teamId, [memberPlayerId], cancellationToken);
            await writer.AddAsync(
                new NotificationDraft(
                    NotificationType.LadderLeadTaken, teamId, $"lead-taken:{scopeKey}:{category}:{matchId}")
                {
                    // No actor, by design: these are system rows, which is precisely what lets the
                    // recorder receive their own lead row on a self-recorded match (§4.2).
                    Category = category,
                    MatchId = matchId,
                    SeasonId = seasonId,
                    SubjectPlayerId = partnerPlayerId,
                },
                recipients,
                cancellationToken);
        }

        // The dethroned side all get the same row: the subject is whoever took it (for a pair, its
        // lower player id — one column cannot hold a duo).
        var previousMemberIds = Members(previousPlayerId, previousPartnerId).Select(m => m.MemberPlayerId);
        var previousRecipients = await NotificationRecipients.ForPlayersAsync(
            db, teamId, previousMemberIds, cancellationToken);
        await writer.AddAsync(
            new NotificationDraft(
                NotificationType.LadderLeadLost, teamId, $"lead-lost:{scopeKey}:{category}:{matchId}")
            {
                Category = category,
                MatchId = matchId,
                SeasonId = seasonId,
                SubjectPlayerId = top.PlayerId,
            },
            previousRecipients,
            cancellationToken);
    }

    /// <summary>The players behind one ladder position, each paired with their partner (solo ladders
    /// yield exactly one member with no partner).</summary>
    private static IEnumerable<(int MemberPlayerId, int? PartnerPlayerId)> Members(int playerId, int? partnerPlayerId)
    {
        yield return (playerId, partnerPlayerId);
        if (partnerPlayerId is int partner)
            yield return (partner, playerId);
    }
}
