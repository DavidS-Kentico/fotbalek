using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.SharedKernel;

namespace Fotbalek.Application.Features.Memberships;

/// <summary>Whether the CURRENT USER is a member of the team.</summary>
public sealed record IsTeamMemberQuery(int TeamId) : IQuery<bool>;

internal sealed class IsTeamMemberQueryHandler(TeamAccess teamAccess)
    : IQueryHandler<IsTeamMemberQuery, bool>
{
    public async Task<Result<bool>> Handle(IsTeamMemberQuery query, CancellationToken cancellationToken) =>
        await teamAccess.IsMemberAsync(query.TeamId, cancellationToken);
}
