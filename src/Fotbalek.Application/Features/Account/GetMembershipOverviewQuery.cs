using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Contracts.Memberships;
using Fotbalek.Contracts.Players;
using Fotbalek.Contracts.Teams;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Account;

/// <summary>The account page's overview: one row per membership of the CURRENT USER.</summary>
public sealed record GetMembershipOverviewQuery : IQuery<List<MembershipOverviewDto>>;

internal sealed class GetMembershipOverviewQueryHandler(IAppDbContext db, IUserContext userContext)
    : IQueryHandler<GetMembershipOverviewQuery, List<MembershipOverviewDto>>
{
    public async Task<Result<List<MembershipOverviewDto>>> Handle(GetMembershipOverviewQuery query, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not int userId)
            return new List<MembershipOverviewDto>();

        return await db.TeamMemberships
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.JoinedAt)
            .Select(m => new MembershipOverviewDto(
                new TeamDto(m.Team.Id, m.Team.Name, m.Team.CodeName, m.Team.CaptainUserId, m.Team.CreatedAt),
                m.JoinedAt,
                m.Team.CaptainUserId == userId,
                // At most one user-Player per team (unique filtered index).
                m.Team.Players
                    .Where(p => p.UserId == userId)
                    .Select(p => new PlayerDto(p.Id, p.TeamId, p.UserId, p.Name, p.AvatarId, p.Elo, p.IsActive, p.CreatedAt))
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);
    }
}
