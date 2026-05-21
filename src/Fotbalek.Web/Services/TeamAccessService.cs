using Fotbalek.Web.Data;
using Fotbalek.Web.Data.Entities;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Web.Services;

/// <summary>
/// Provides access to the current team based on the URL route, scoped to the authenticated user.
/// </summary>
public class TeamAccessService(
    NavigationManager navigation,
    CurrentUserService currentUser,
    AppDbContext db)
{
    private Team? _cachedTeam;
    private string? _cachedCode;
    private int? _cachedUserId;

    /// <summary>
    /// Returns the team from the URL if the current user is a member. Null otherwise.
    /// </summary>
    public async Task<Team?> GetCurrentTeamAsync()
    {
        var code = GetTeamCodeFromUrl();
        if (string.IsNullOrEmpty(code)) return null;

        var userId = await currentUser.GetUserIdAsync();
        if (userId == null) return null;

        if (_cachedTeam != null &&
            string.Equals(_cachedCode, code, StringComparison.OrdinalIgnoreCase) &&
            _cachedUserId == userId)
        {
            return _cachedTeam;
        }

        var lower = code.ToLowerInvariant();
        var team = await db.Teams
            .FirstOrDefaultAsync(t => t.CodeName == lower);
        if (team == null) return null;

        var isMember = await db.TeamMemberships
            .AnyAsync(m => m.TeamId == team.Id && m.UserId == userId);
        if (!isMember) return null;

        _cachedTeam = team;
        _cachedCode = code;
        _cachedUserId = userId;
        return team;
    }

    public async Task<bool> IsAdminAsync()
    {
        var team = await GetCurrentTeamAsync();
        var userId = await currentUser.GetUserIdAsync();
        return team != null && userId != null && team.AdminUserId == userId;
    }

    public async Task<bool> IsAdminAsync(Team team)
    {
        var userId = await currentUser.GetUserIdAsync();
        return userId != null && team.AdminUserId == userId;
    }

    public string? GetTeamCodeFromUrl()
    {
        var uri = new Uri(navigation.Uri);
        var path = uri.AbsolutePath.TrimStart('/');
        var segments = path.Split('/');
        if (segments.Length == 0) return null;
        var first = segments[0];
        // Reserved root routes that shouldn't be treated as team codes
        if (string.IsNullOrEmpty(first) ||
            first.Equals("login", StringComparison.OrdinalIgnoreCase) ||
            first.Equals("register", StringComparison.OrdinalIgnoreCase) ||
            first.Equals("profile", StringComparison.OrdinalIgnoreCase) ||
            first.Equals("teams", StringComparison.OrdinalIgnoreCase) ||
            first.Equals("create", StringComparison.OrdinalIgnoreCase) ||
            first.Equals("join", StringComparison.OrdinalIgnoreCase) ||
            first.Equals("account", StringComparison.OrdinalIgnoreCase) ||
            first.Equals("not-found", StringComparison.OrdinalIgnoreCase))
            return null;
        return first;
    }
}
