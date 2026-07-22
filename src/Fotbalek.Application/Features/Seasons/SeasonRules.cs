using System.Linq.Expressions;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Domain.Entities;
using Fotbalek.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Seasons;

/// <summary>Shared season invariants (name uniqueness, period overlap) and input normalization.</summary>
internal static class SeasonRules
{
    /// <summary>
    /// SQL-translatable "currently accepting matches" predicate — the query-side mirror of
    /// <see cref="Season.IsActiveAt"/>, which EF Core can't translate. Keep the two in sync.
    /// </summary>
    public static Expression<Func<Season, bool>> ActiveAt(DateTimeOffset now) =>
        s => s.ClosedAt == null && s.StartsAt <= now && (s.EndsAt == null || now < s.EndsAt);

    public static Error? ValidateNameAndDescription(ref string name, ref string? description)
    {
        name = name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 100)
            return Error.Validation("Seasons.InvalidName", "The season name must be 1-100 characters.");
        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (description?.Length > 500)
            return Error.Validation("Seasons.InvalidDescription", "The description must be at most 500 characters.");
        return null;
    }

    /// <summary>
    /// Season names are unique per team (case-insensitive, trimmed) — the name is the human
    /// identifier in selectors, match chips and trophy cases, where the period is not shown.
    /// Checked on create (under the team timeline lock) and on rename (excluding the season itself).
    /// </summary>
    public static async Task<Error?> CheckNameAvailableAsync(
        IAppDbContext db, int teamId, string name, int? excludeSeasonId, CancellationToken cancellationToken)
    {
        var normalized = name.ToLowerInvariant();
        var taken = await db.Seasons.AnyAsync(s =>
            s.TeamId == teamId &&
            (excludeSeasonId == null || s.Id != excludeSeasonId) &&
            s.Name.ToLower() == normalized,
            cancellationToken);
        return taken
            ? Error.Conflict("Seasons.NameTaken", $"A season named \"{name}\" already exists in this team.")
            : null;
    }

    /// <summary>
    /// Season periods [StartsAt, EndsAt) of a team must not overlap. An open-ended season extends
    /// to infinity for this check — so while one is open-ended, no later season can be created;
    /// a season entirely in the past can always be added for backfill.
    /// </summary>
    public static async Task<Error?> CheckNoOverlapAsync(
        IAppDbContext db, int teamId, int? excludeSeasonId, DateTimeOffset startsAt, DateTimeOffset? endsAt,
        CancellationToken cancellationToken)
    {
        var blocking = await db.Seasons
            .Where(s => s.TeamId == teamId && (excludeSeasonId == null || s.Id != excludeSeasonId))
            .Where(s => (endsAt == null || s.StartsAt < endsAt) && (s.EndsAt == null || startsAt < s.EndsAt))
            .Select(s => new { s.Name, s.EndsAt })
            .FirstOrDefaultAsync(cancellationToken);

        if (blocking == null)
            return null;

        var hint = blocking.EndsAt == null
            ? " End the current season (or set its end date) first."
            : string.Empty;
        return Error.Conflict("Seasons.Overlap", $"The period overlaps season \"{blocking.Name}\".{hint}");
    }
}
