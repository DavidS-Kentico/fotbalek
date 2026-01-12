using Fotbalek.Web.Data.Entities;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace Fotbalek.Web.Services;

public class AuthService(TeamService teamService, ProtectedLocalStorage localStorage)
{
    private const string AuthKey = "fotbalek_auth";
    private Team? _currentTeam;

    public Team? CurrentTeam => _currentTeam;

    public async Task<bool> LoginAsync(string codeName, string password)
    {
        if (!await teamService.ValidatePasswordAsync(codeName, password))
            return false;

        var team = await teamService.GetByCodeNameAsync(codeName);
        if (team == null)
            return false;

        await localStorage.SetAsync(AuthKey, new AuthData
        {
            TeamId = team.Id,
            CodeName = team.CodeName
        });

        _currentTeam = team;
        return true;
    }

    public async Task LogoutAsync()
    {
        await localStorage.DeleteAsync(AuthKey);
        _currentTeam = null;
    }

    public async Task<Team?> GetCurrentTeamAsync()
    {
        if (_currentTeam != null)
            return _currentTeam;

        try
        {
            var result = await localStorage.GetAsync<AuthData>(AuthKey);
            if (!result.Success || result.Value == null)
                return null;

            _currentTeam = await teamService.GetByIdAsync(result.Value.TeamId);
            return _currentTeam;
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> IsAuthenticatedForTeamAsync(string codeName)
    {
        var team = await GetCurrentTeamAsync();
        return team != null && team.CodeName.Equals(codeName, StringComparison.OrdinalIgnoreCase);
    }

    public async Task SetCurrentTeamAsync(Team team)
    {
        await localStorage.SetAsync(AuthKey, new AuthData
        {
            TeamId = team.Id,
            CodeName = team.CodeName
        });
        _currentTeam = team;
    }

    private class AuthData
    {
        public int TeamId { get; set; }
        public string CodeName { get; set; } = string.Empty;
    }
}
