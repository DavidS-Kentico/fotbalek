using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Features.Seasons;
using Fotbalek.Application.Features.Teams;
using Fotbalek.Contracts.Teams;
using Microsoft.AspNetCore.Components;

namespace Fotbalek.Web.Services;

/// <summary>
/// Provides access to the current team based on the URL route, scoped to the authenticated user
/// (replaces the old TeamAccessService — the membership check itself moved into the dispatched
/// query; URL parsing, the per-circuit cache and the lazy season-close dispatch stay here, §3).
/// </summary>
public class CurrentTeamProvider(
    NavigationManager navigation,
    CurrentUserAccessor currentUser,
    IScopedSender sender,
    ILogger<CurrentTeamProvider> logger)
{
    private TeamDto? _cachedTeam;
    private string? _cachedCode;
    private int? _cachedUserId;

    /// <summary>
    /// Returns the team from the URL if the current user is a member. Null otherwise.
    /// </summary>
    public async Task<TeamDto?> GetCurrentTeamAsync()
    {
        var code = GetTeamCodeFromUrl();
        if (string.IsNullOrEmpty(code)) return null;

        var userId = await currentUser.GetUserIdAsync();
        if (userId == null) return null;

        if (_cachedTeam != null &&
            string.Equals(_cachedCode, code, StringComparison.OrdinalIgnoreCase) &&
            _cachedUserId == userId)
        {
            // The lazy-close check must run before the cache fast-path — a check placed after it
            // would fire at most once per (potentially hours-long) Blazor circuit.
            await CloseDueSeasonsAsync(_cachedTeam.Id);
            return _cachedTeam;
        }

        var result = await sender.Send(new GetTeamForMemberQuery(code));
        var team = result.IsSuccess ? result.Value : null;
        if (team == null) return null;

        // Lazy close: seasons past their end date are closed by the first page load — a system
        // action triggered by any member, not a captain action.
        await CloseDueSeasonsAsync(team.Id);

        _cachedTeam = team;
        _cachedCode = code;
        _cachedUserId = userId;
        return team;
    }

    public async Task<bool> IsCaptainAsync()
    {
        var team = await GetCurrentTeamAsync();
        var userId = await currentUser.GetUserIdAsync();
        return team != null && userId != null && team.CaptainUserId == userId;
    }

    public async Task<bool> IsCaptainAsync(TeamDto team)
    {
        var userId = await currentUser.GetUserIdAsync();
        return userId != null && team.CaptainUserId == userId;
    }

    /// <summary>The cached team with an updated captain — used after a successful captain claim.</summary>
    public void UpdateCachedCaptain(int userId)
    {
        if (_cachedTeam != null)
            _cachedTeam = _cachedTeam with { CaptainUserId = userId };
    }

    public string? GetTeamCodeFromUrl()
    {
        var uri = new Uri(navigation.Uri);
        var segments = uri.AbsolutePath.TrimStart('/').Split('/');
        // Team pages live under /team/{codename}; anything else is not a team URL.
        if (segments.Length < 2 || !segments[0].Equals("team", StringComparison.OrdinalIgnoreCase))
            return null;
        var code = segments[1];
        return string.IsNullOrEmpty(code) ? null : code;
    }

    /// <summary>
    /// Lazy close (system action, triggered by any member's page load): closes every season of the
    /// team past its end date, each in its own dispatch/transaction. Failures are logged, never
    /// propagated to the page load.
    /// </summary>
    private async Task CloseDueSeasonsAsync(int teamId)
    {
        var dueIds = await sender.Send(new GetDueSeasonIdsQuery(teamId));
        if (dueIds.IsFailure) return;

        foreach (var seasonId in dueIds.Value)
        {
            try
            {
                var result = await sender.Send(new CloseSeasonCommand(seasonId));
                if (result.IsFailure)
                    logger.LogError("Lazy close of season {SeasonId} failed: {Error}", seasonId, result.Error.Code);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Lazy close of season {SeasonId} failed", seasonId);
            }
        }
    }
}
