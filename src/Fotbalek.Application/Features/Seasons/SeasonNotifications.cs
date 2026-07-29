using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Features.Notifications;
using Fotbalek.Domain.Entities;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Seasons;

/// <summary>
/// The season-lifecycle notification writes, shared by the handlers that trigger them: two close
/// paths (lazy close and captain-ends-early) and two start paths (immediate creation and the lazy
/// announce hook) — AI/notifications.md §5.4, §5.5.
/// <para>
/// Adds tracked rows only — <b>the caller must SaveChanges</b> (§4.1).
/// </para>
/// </summary>
internal static class SeasonNotifications
{
    /// <param name="actorUserId">The creator, on the immediate-start path — they are excluded from
    /// the recipients. Null on the lazy path, which has no actor: it fires on whoever happened to
    /// open a team page first.</param>
    public static async Task WriteStartedAsync(
        IAppDbContext db,
        INotificationWriter writer,
        Season season,
        int? actorUserId,
        CancellationToken cancellationToken)
    {
        var recipients = await NotificationRecipients.ForTeamAsync(db, season.TeamId, cancellationToken);
        await writer.AddAsync(
            new NotificationDraft(
                NotificationType.SeasonStarted, season.TeamId, $"season-started:{season.Id}")
            {
                ActorUserId = actorUserId,
                SeasonId = season.Id,
            },
            recipients,
            cancellationToken);
    }

    /// <summary>
    /// The result row for every claimed member plus one row per award won. The close is a documented
    /// SYSTEM action with no captain check — the lazy close is triggered by an arbitrary member's page
    /// load — and this write inherits that stance, so it carries no actor (§5.5).
    /// </summary>
    public static async Task WriteCloseAsync(
        IAppDbContext db,
        INotificationWriter writer,
        Season season,
        SeasonCloseResult result,
        CancellationToken cancellationToken)
    {
        var claimed = await db.Players
            .AsNoTracking()
            .Where(p => p.TeamId == season.TeamId && p.UserId != null)
            .Select(p => new { PlayerId = p.Id, UserId = p.UserId!.Value })
            .ToListAsync(cancellationToken);
        if (claimed.Count == 0)
            return;

        var rankByPlayer = result.Ranks.ToDictionary(r => r.PlayerId, r => r.FinalRank);

        // Value is the RECIPIENT's own final rank, so the members are grouped by it rather than
        // written one by one. Null covers both "did not participate" and "inactive at close"; Web
        // words those two the same way (§5.5, §10).
        foreach (var group in claimed.GroupBy(p => rankByPlayer.GetValueOrDefault(p.PlayerId)))
        {
            await writer.AddAsync(
                new NotificationDraft(
                    NotificationType.SeasonEnded, season.TeamId, $"season-ended:{season.Id}")
                {
                    SeasonId = season.Id,
                    Value = group.Key,
                },
                group.Select(p => p.UserId),
                cancellationToken);
        }

        var userIdByPlayer = claimed.ToDictionary(p => p.PlayerId, p => p.UserId);

        // A pair award produces two SeasonAward rows sharing a (Category, Rank) — and therefore a
        // dedup key — but they go to different users, and the key is unique per (UserId, DedupKey).
        foreach (var award in result.Awards)
        {
            if (!userIdByPlayer.TryGetValue(award.PlayerId, out var userId))
                continue;

            await writer.AddAsync(
                new NotificationDraft(
                    NotificationType.SeasonAward, season.TeamId,
                    $"award:{season.Id}:{award.Category}:{award.Rank}")
                {
                    SeasonId = season.Id,
                    Category = award.Category,
                    Value = award.Rank,
                    SubjectPlayerId = award.PartnerPlayerId,
                },
                [userId],
                cancellationToken);
        }
    }
}
