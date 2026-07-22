using Fotbalek.Application.Common.Abstractions;
using Fotbalek.SharedKernel;
using MediatR;

namespace Fotbalek.Application.Common.Behaviors;

/// <summary>
/// Wraps EVERY command (the ICommandBase marker; queries are never wrapped — MS DI skips this
/// behavior for requests that don't satisfy the constraint) in a database transaction.
/// Wrapping even single-SaveChanges commands is deliberate: it guarantees (a) an open transaction
/// whenever a handler takes an IDbLocks lock, and (b) exactly one commit point per dispatch for
/// the post-commit event flush (AI/architecture.md §4.2).
///
/// Nested dispatches in the same scope see an active transaction and pass through — the OWNER
/// commits and flushes the whole scope's event queue. Events flush AFTER commit, synchronously,
/// so the sender's own circuit keeps seeing its event as part of the round trip. A failed
/// handler result, failed commit, or exception discards the queue.
/// </summary>
public sealed class TransactionBehavior<TRequest, TResponse>(
    IUnitOfWork unitOfWork,
    IEventCollector eventCollector,
    IPublisher publisher)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommandBase
    where TResponse : Result
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Joined (nested) dispatch: the outer behavior owns commit + flush.
        if (unitOfWork.HasActiveTransaction)
            return await next(cancellationToken);

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        TResponse response;
        try
        {
            response = await next(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            eventCollector.Discard();
            throw;
        }

        if (response.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken);
            eventCollector.Discard();
            return response;
        }

        await transaction.CommitAsync(cancellationToken);

        // Post-commit flush — a publish before commit would fan out state that can roll back
        // (ghost chat messages on every subscribed circuit).
        foreach (var notification in eventCollector.Drain())
        {
            await publisher.Publish(notification, cancellationToken);
        }

        return response;
    }
}
