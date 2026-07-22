using MediatR;

namespace Fotbalek.Application.Common.Abstractions;

/// <summary>
/// Scoped queue of domain events raised during a command. Handlers never publish mid-handler —
/// they enqueue here, and the TransactionBehavior that OWNS the transaction publishes the queue
/// via IPublisher immediately after a successful commit (still synchronously, before the dispatch
/// returns). A failed handler/commit discards the queue. See AI/architecture.md §4.2.
/// </summary>
public interface IEventCollector
{
    void Enqueue(INotification notification);

    /// <summary>Removes and returns everything queued so far (post-commit flush).</summary>
    IReadOnlyList<INotification> Drain();

    /// <summary>Drops everything queued so far (failed handler / rollback).</summary>
    void Discard();
}
