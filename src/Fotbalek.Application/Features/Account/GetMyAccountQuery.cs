using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Contracts.Account;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Account;

/// <summary>The CURRENT USER's account facts (null when anonymous).</summary>
public sealed record GetMyAccountQuery : IQuery<AccountInfoDto?>;

internal sealed class GetMyAccountQueryHandler(IAppDbContext db, IUserContext userContext)
    : IQueryHandler<GetMyAccountQuery, AccountInfoDto?>
{
    public async Task<Result<AccountInfoDto?>> Handle(GetMyAccountQuery query, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not int userId)
            return Result.Success<AccountInfoDto?>(null);
        return await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new AccountInfoDto(u.Id, u.UserName, u.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
