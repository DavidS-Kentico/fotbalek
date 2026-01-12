using System.Security.Cryptography;
using Fotbalek.Web.Data;
using Fotbalek.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Web.Services;

public class ShareTokenService(AppDbContext db)
{
    public async Task<string> GenerateTokenAsync(int teamId)
    {
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

    public async Task CleanupExpiredTokensAsync()
    {
        var expiredTokens = await db.ShareTokens
            .Where(st => st.ExpiresAt <= DateTimeOffset.UtcNow)
            .ToListAsync();

        if (expiredTokens.Count > 0)
        {
            db.ShareTokens.RemoveRange(expiredTokens);
            await db.SaveChangesAsync();
        }
    }
}
