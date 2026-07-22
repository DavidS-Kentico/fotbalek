using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Contracts.Matches;
using Fotbalek.SharedKernel;

namespace Fotbalek.Application.Features.Matches;

/// <summary>Rule-only deletability (time window / closed season / later matches), with the reason.</summary>
public sealed record CanDeleteMatchQuery(int MatchId) : IQuery<MatchDeletabilityDto>;

internal sealed class CanDeleteMatchQueryHandler(IAppDbContext db, TeamAccess teamAccess)
    : IQueryHandler<CanDeleteMatchQuery, MatchDeletabilityDto>
{
    public async Task<Result<MatchDeletabilityDto>> Handle(CanDeleteMatchQuery query, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberOfMatchTeamAsync(query.MatchId, cancellationToken))
            return Result.Failure<MatchDeletabilityDto>(CommonErrors.NotMember);

        var (canDelete, reason) = await MatchRules.CanDeleteWithReasonAsync(db, query.MatchId, cancellationToken);
        return new MatchDeletabilityDto(canDelete, reason);
    }
}
