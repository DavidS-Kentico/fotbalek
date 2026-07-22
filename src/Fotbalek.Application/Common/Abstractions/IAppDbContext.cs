using Fotbalek.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Fotbalek.Application.Common.Abstractions;

/// <summary>
/// The persistence surface handlers program against. Implemented by Infrastructure's AppDbContext;
/// registered scoped — one context per dispatch scope (see AI/architecture.md §4.3).
/// </summary>
public interface IAppDbContext
{
    DbSet<AppUser> Users { get; }
    DbSet<Team> Teams { get; }
    DbSet<Player> Players { get; }
    DbSet<Match> Matches { get; }
    DbSet<MatchPlayer> MatchPlayers { get; }
    DbSet<ShareToken> ShareTokens { get; }
    DbSet<TeamMembership> TeamMemberships { get; }
    DbSet<Season> Seasons { get; }
    DbSet<SeasonPlayer> SeasonPlayers { get; }
    DbSet<SeasonPlayerResult> SeasonPlayerResults { get; }
    DbSet<SeasonPair> SeasonPairs { get; }
    DbSet<SeasonAward> SeasonAwards { get; }
    DbSet<ChatMessage> ChatMessages { get; }
    DbSet<ChatMessageReaction> ChatMessageReactions { get; }
    DbSet<ChatReadState> ChatReadStates { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Change-tracker access for the few handlers that reload or detach entities
    /// (unique-index race recovery, re-reads under a row lock).</summary>
    EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;
}
