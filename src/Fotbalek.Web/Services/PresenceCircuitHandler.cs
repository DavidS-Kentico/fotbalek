using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Fotbalek.Web.Services;

/// <summary>
/// Tracks per-circuit user presence. One circuit corresponds to a single browser tab/connection;
/// a user with multiple tabs is counted multiple times so the count only drops to zero once they're all closed.
/// </summary>
public class PresenceCircuitHandler(
    PresenceTracker tracker,
    AuthenticationStateProvider authStateProvider) : CircuitHandler
{
    private int? _userId;

    public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        var authState = await authStateProvider.GetAuthenticationStateAsync();
        var idStr = authState.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(idStr, out var userId))
        {
            _userId = userId;
            tracker.Track(userId);
        }
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        if (_userId is { } userId)
        {
            tracker.Untrack(userId);
            _userId = null;
        }
        return Task.CompletedTask;
    }
}
