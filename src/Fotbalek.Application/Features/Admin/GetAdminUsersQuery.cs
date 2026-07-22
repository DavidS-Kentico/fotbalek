using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Contracts.Admin;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Admin;

/// <summary>
/// The admin user list. Re-verifies the actor via IUserContext.IsAdmin — the admin pages gate
/// rendering with the admin policy, but the handler must not skip the actor check.
/// </summary>
public sealed record GetAdminUsersQuery : IQuery<List<AdminUserRowDto>>;

internal sealed class GetAdminUsersQueryHandler(IAppDbContext db, IUserContext userContext)
    : IQueryHandler<GetAdminUsersQuery, List<AdminUserRowDto>>
{
    public async Task<Result<List<AdminUserRowDto>>> Handle(GetAdminUsersQuery query, CancellationToken cancellationToken)
    {
        if (!userContext.IsAdmin)
            return Result.Failure<List<AdminUserRowDto>>(CommonErrors.NotAdmin);

        return await db.Users
            .AsNoTracking()
            .OrderBy(u => u.UserName)
            .Select(u => new AdminUserRowDto(
                u.Id,
                u.UserName,
                u.CreatedAt,
                u.Memberships.Count,
                u.Players.Count,
                u.LockoutEnd))
            .ToListAsync(cancellationToken);
    }
}
