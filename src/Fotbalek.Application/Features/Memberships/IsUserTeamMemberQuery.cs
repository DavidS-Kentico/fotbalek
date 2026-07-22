using Fotbalek.Application.Common.Abstractions;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Memberships;

/// <summary>Whether a SPECIFIC user is a member of the team — the GameHub join path (§4.4).</summary>
public sealed record IsUserTeamMemberQuery(int TeamId, int UserId) : IQuery<bool>;

internal sealed class IsUserTeamMemberQueryHandler(IAppDbContext db)
    : IQueryHandler<IsUserTeamMemberQuery, bool>
{
    public async Task<Result<bool>> Handle(IsUserTeamMemberQuery query, CancellationToken cancellationToken) =>
        await db.TeamMemberships.AnyAsync(
            m => m.UserId == query.UserId && m.TeamId == query.TeamId, cancellationToken);
}
