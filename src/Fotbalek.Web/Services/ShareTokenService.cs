using System.Security.Cryptography;
using Fotbalek.Web.Data;
using Fotbalek.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Web.Services;

public class ShareTokenService(AppDbContext db)
{
    /// <summary>
    /// Generates a share link for a team. The caller must be a member of that team.
    /// Returns null when the caller is not a member.
    /// </summary>
    public async Task<string?> GenerateTokenAsync(int teamId, int actorUserId)
    {
        var isMember = await db.TeamMemberships
            .AnyAsync(m => m.TeamId == teamId && m.UserId == actorUserId);
        if (!isMember) return null;

        // Generate a secure random token
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(tokenBytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');

        var shareToken = new ShareToken
        {
            TeamId = teamId,
            Token = token,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(Constants.TimeThresholds.ShareTokenExpirationHours),
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.ShareTokens.Add(shareToken);
        await db.SaveChangesAsync();

        return token;
    }

    public async Task<Team?> ValidateTokenAsync(string token)
    {
        var shareToken = await db.ShareTokens
            .Include(st => st.Team)
            .FirstOrDefaultAsync(st => st.Token == token && st.ExpiresAt > DateTimeOffset.UtcNow);

        return shareToken?.Team;
    }
}
