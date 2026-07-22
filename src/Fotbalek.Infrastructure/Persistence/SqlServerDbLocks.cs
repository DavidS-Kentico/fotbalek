using Fotbalek.Application.Common.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Infrastructure.Persistence;

/// <summary>
/// SQL Server pessimistic locks over the scoped AppDbContext, so the SQL runs on the handler's
/// own connection and joins the ambient transaction (which the TransactionBehavior guarantees —
/// both locks are Transaction-owned and would fail without one).
/// </summary>
public sealed class SqlServerDbLocks(AppDbContext db) : IDbLocks
{
    public async Task LockSeasonRowAsync(int seasonId, CancellationToken cancellationToken = default)
    {
        await db.Database.ExecuteSqlAsync(
            $"SELECT Id FROM Seasons WITH (UPDLOCK, ROWLOCK) WHERE Id = {seasonId}",
            cancellationToken);
    }

    public async Task AcquireTeamTimelineLockAsync(int teamId, CancellationToken cancellationToken = default)
    {
        var resource = $"fotbalek-season-timeline-{teamId}";
        await db.Database.ExecuteSqlAsync($@"
DECLARE @lockResult int;
EXEC @lockResult = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
IF @lockResult < 0 THROW 51000, 'Could not acquire the season timeline lock.', 1;",
            cancellationToken);
    }
}
