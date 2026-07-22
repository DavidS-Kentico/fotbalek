using System.Security.Claims;
using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.SharedKernel;
using Fotbalek.Web.Auth;
using MediatR;

namespace Fotbalek.Web.Services;

/// <summary>
/// The scope-per-dispatch core (§4.3): creates a fresh DI scope, seeds the scope's UserContext
/// from the given principal (null = anonymous), resolves ISender inside that scope, and sends.
/// One scope = one DbContext = one unit of work. Singleton — callers supply the principal
/// (circuit auth state via <see cref="ScopedSender"/>, Context.User in hubs).
/// </summary>
public sealed class ScopedDispatcher(IServiceScopeFactory scopeFactory)
{
    public async Task<Result> Send(ICommand command, ClaimsPrincipal? principal, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        Seed(scope.ServiceProvider, principal);
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, cancellationToken);
    }

    public async Task<Result<TResponse>> Send<TResponse>(ICommand<TResponse> command, ClaimsPrincipal? principal, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        Seed(scope.ServiceProvider, principal);
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, cancellationToken);
    }

    public async Task<Result<TResponse>> Send<TResponse>(IQuery<TResponse> query, ClaimsPrincipal? principal, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        Seed(scope.ServiceProvider, principal);
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(query, cancellationToken);
    }

    private static void Seed(IServiceProvider scopeServices, ClaimsPrincipal? principal)
    {
        var userContext = scopeServices.GetRequiredService<UserContext>();
        userContext.UserId = ParseUserId(principal);
        userContext.IsAdmin = principal?.HasClaim(AdminAuth.ClaimType, "true") == true;
    }

    internal static int? ParseUserId(ClaimsPrincipal? principal)
    {
        var idStr = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(idStr, out var userId) ? userId : null;
    }
}

/// <summary>
/// The circuit-facing <see cref="IScopedSender"/>: resolves the caller's principal from the
/// circuit's AuthenticationStateProvider per dispatch and delegates to <see cref="ScopedDispatcher"/>.
/// Registered scoped — lives for the circuit, but every Send still gets its own fresh scope.
/// </summary>
public sealed class ScopedSender(
    ScopedDispatcher dispatcher,
    Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider authStateProvider)
    : IScopedSender
{
    public async Task<Result> Send(ICommand command, CancellationToken cancellationToken = default) =>
        await dispatcher.Send(command, await GetPrincipalAsync(), cancellationToken);

    public async Task<Result<TResponse>> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default) =>
        await dispatcher.Send(command, await GetPrincipalAsync(), cancellationToken);

    public async Task<Result<TResponse>> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default) =>
        await dispatcher.Send(query, await GetPrincipalAsync(), cancellationToken);

    private async Task<ClaimsPrincipal?> GetPrincipalAsync()
    {
        var authState = await authStateProvider.GetAuthenticationStateAsync();
        return authState.User;
    }
}
