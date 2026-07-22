using Fotbalek.SharedKernel;
using MediatR;

namespace Fotbalek.Application.Common.Abstractions;

// Our own command/query abstractions wrap MediatR so a future mediator swap touches only this
// file and DI setup (documented exception: the notification path — see EventCollector).

/// <summary>Non-generic marker shared by both command shapes — the TransactionBehavior target.</summary>
public interface ICommandBase;

/// <summary>A state-changing request with no payload on success.</summary>
public interface ICommand : IRequest<Result>, ICommandBase;

/// <summary>A state-changing request returning <typeparamref name="TResponse"/> on success.</summary>
public interface ICommand<TResponse> : IRequest<Result<TResponse>>, ICommandBase;

/// <summary>A read-only request. Never wrapped in a transaction.</summary>
public interface IQuery<TResponse> : IRequest<Result<TResponse>>;

public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand, Result>
    where TCommand : ICommand;

public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse>;

public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>;
