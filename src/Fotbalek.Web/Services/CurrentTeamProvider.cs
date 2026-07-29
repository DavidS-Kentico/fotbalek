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
            // The lazy season hooks must run before the cache fast-path — a check placed after it
            // would fire at most once per (potentially hours-long) Blazor circuit.
            await RunSeasonHooksAsync(_cachedTeam.Id);
            return _cachedTeam;
        }

        var result = await sender.Send(new GetTeamForMemberQuery(code));
        var team = result.IsSuccess ? result.Value : null;
        if (team == null) return null;

        // Lazy season hooks: seasons past their end date are closed, and seasons that have started
        // are announced, by the first page load — system actions triggered by any member, not
        // captain actions.
        await RunSeasonHooksAsync(team.Id);

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
    /// The lazy season hooks (system actions, triggered by any member's page load), in one lookup:
    /// close every season past its end date, then announce every season that has started without
    /// being announced — the app has no scheduler, so nothing runs at StartsAt (AI/notifications.md
    /// §5.4). Each runs in its own dispatch/transaction; failures are logged, never propagated to
    /// the page load.
    /// <para>
    /// ⚠ <b>Close first.</b> A season can run its whole course between two visits — scheduled,
    /// started, ended, all while nobody opened a team page. Closing first lets the announce command's
    /// ClosedAt re-check suppress the start announcement, so only "ended" is delivered.
    /// </para>
    /// </summary>
    private async Task RunSeasonHooksAsync(int teamId)
    {
        var hooks = await sender.Send(new GetTeamSeasonHooksQuery(teamId));
        if (hooks.IsFailure) return;

        foreach (var seasonId in hooks.Value.DueClose)
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

        if (hooks.Value.Unannounced.Count == 0) return;

        try
        {
            var result = await sender.Send(new AnnounceStartedSeasonsCommand(teamId));
            if (result.IsFailure)
                logger.LogError(
                    "Lazy season-start announcement for team {TeamId} failed: {Error}", teamId, result.Error.Code);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lazy season-start announcement for team {TeamId} failed", teamId);
        }
    }
}
