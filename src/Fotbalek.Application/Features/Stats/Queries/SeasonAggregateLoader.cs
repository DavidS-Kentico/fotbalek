using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Domain.Entities;
using Fotbalek.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Application.Features.Stats.Queries;

/// <summary>Loads a season's ladder + live per-player aggregates — shared by the live season views.</summary>
internal static class SeasonAggregateLoader
{
    public static async Task<(List<SeasonPlayer> Ladder, Dictionary<int, SeasonAggregates.ParticipantAggregate> Aggregates)>
        LoadLiveAsync(IAppDbContext db, int seasonId, CancellationToken cancellationToken)
    {
        var matches = await db.Matches
            .AsNoTracking()
            .Include(m => m.MatchPlayers)
            .Where(m => m.SeasonId == seasonId)
            .OrderBy(m => m.PlayedAt).ThenBy(m => m.Id)
            .ToListAsync(cancellationToken);

        var ladder = await db.SeasonPlayers
            .AsNoTracking()
            .Include(sp => sp.Player)
            .Where(sp => sp.SeasonId == seasonId)
            .ToListAsync(cancellationToken);

        return (ladder, SeasonAggregates.ComputeParticipants(matches));
    }
}
