using Microsoft.EntityFrameworkCore.Storage;

namespace Fotbalek.Application.Common.Abstractions;

/// <summary>
/// Transaction control over the scope's DbContext. Used exclusively by the TransactionBehavior;
/// handlers never manage transactions themselves.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>True when a transaction is already open on the scope's context — a nested
    /// dispatch must join it, not begin a second one.</summary>
    bool HasActiveTransaction { get; }

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}
