using Fotbalek.SharedKernel;

namespace Fotbalek.Application.Common.Abstractions;

/// <summary>
/// The single dispatch entry point for long-lived callers (Blazor circuits, SignalR hubs):
/// each Send creates a fresh DI scope, seeds the scope's <see cref="IUserContext"/> from the
/// caller, and dispatches inside that scope — one scope = one DbContext = one unit of work
/// (see AI/architecture.md §4.3).
/// </summary>
public interface IScopedSender
{
    Task<Result> Send(ICommand command, CancellationToken cancellationToken = default);
    Task<Result<TResponse>> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default);
    Task<Result<TResponse>> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default);
}
