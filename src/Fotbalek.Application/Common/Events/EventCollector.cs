using Fotbalek.Application.Common.Abstractions;
using MediatR;

namespace Fotbalek.Application.Common.Events;

/// <summary>Scoped in-memory queue — see <see cref="IEventCollector"/> for the contract.</summary>
public sealed class EventCollector : IEventCollector
{
    private readonly List<INotification> _queue = [];

    public void Enqueue(INotification notification) => _queue.Add(notification);

    public IReadOnlyList<INotification> Drain()
    {
        var drained = _queue.ToArray();
        _queue.Clear();
        return drained;
    }

    public void Discard() => _queue.Clear();
}
