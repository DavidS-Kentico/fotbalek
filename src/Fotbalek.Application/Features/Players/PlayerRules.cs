using Fotbalek.Application.Common.Abstractions;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Players;

/// <summary>Shared player invariants: the name-uniqueness rule and its error.</summary>
internal static class PlayerRules
{
    public const int NameMaxLength = 50;

    public static async Task<bool> IsNameTakenAsync(
        IAppDbContext db, int teamId, string name, int? excludePlayerId, CancellationToken cancellationToken)
    {
        var normalized = name.ToLowerInvariant();
        var players = db.Players.Where(p => p.TeamId == teamId && p.Name.ToLower() == normalized);
        if (excludePlayerId is int excluded)
            players = players.Where(p => p.Id != excluded);
        return await players.AnyAsync(cancellationToken);
    }

    public static readonly Error NameTaken =
        Error.Conflict("Players.NameTaken", "A player with this name already exists.");

    /// <summary>True when the user already owns a player in the team — at most one claimed player per user per team.</summary>
    public static Task<bool> HasClaimedPlayerAsync(
        IAppDbContext db, int teamId, int userId, CancellationToken cancellationToken) =>
        db.Players.AnyAsync(p => p.TeamId == teamId && p.UserId == userId, cancellationToken);
}
