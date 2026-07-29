using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Contracts.Seasons;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Seasons;

/// <summary>
/// Both lazy per-team season hooks in one dispatch: which seasons are due to close, and which have
/// started without being announced. The host loops over each (AI/architecture.md §3,
/// AI/notifications.md §5.4).
/// <para>
/// Folded into one query rather than two dispatches deliberately: the host runs this on EVERY
/// current-team resolution — including the cached fast path, so the check cannot fire just once per
/// multi-hour circuit — and a second dispatch beside it would double a cost that is already paid more
/// often than it looks. The two lists stay separate SELECTs so each rides its own index:
/// (TeamId, ClosedAt, EndsAt) for the first, (TeamId, StartAnnouncedAt, StartsAt) for the second.
/// </para>
/// <para>
/// Member-gated: both hooks are triggered by a member's page load, after team resolution.
/// </para>
/// </summary>
public sealed record GetTeamSeasonHooksQuery(int TeamId) : IQuery<TeamSeasonHooksDto>;

internal sealed class GetTeamSeasonHooksQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<GetTeamSeasonHooksQuery, TeamSeasonHooksDto>
{
    public async Task<Result<TeamSeasonHooksDto>> Handle(
        GetTeamSeasonHooksQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberAsync(query.TeamId, cancellationToken))
            return Result.Failure<TeamSeasonHooksDto>(CommonErrors.NotMember);

        var now = DateTimeOffset.UtcNow;

        var dueClose = await db.Seasons
            .AsNoTracking()
            .Where(s => s.TeamId == query.TeamId && s.ClosedAt == null && s.EndsAt != null && s.EndsAt <= now)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        // ClosedAt == null is not redundant here: a season that ran its whole course unvisited gets
        // closed by the loop above and never stamps StartAnnouncedAt, so without this filter it would
        // be returned — and pointlessly re-dispatched — on every page load forever (§5.4).
        var unannounced = await db.Seasons
            .AsNoTracking()
            .Where(s => s.TeamId == query.TeamId
                && s.StartAnnouncedAt == null
                && s.StartsAt <= now
                && s.ClosedAt == null)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        return new TeamSeasonHooksDto(dueClose, unannounced);
    }
}
