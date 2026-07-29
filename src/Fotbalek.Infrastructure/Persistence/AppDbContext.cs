using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Domain.Entities;
using Fotbalek.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Fotbalek.Infrastructure.Persistence;

public class AppDbContext : IdentityDbContext<AppUser, IdentityRole<int>, int>, IAppDbContext, IUnitOfWork
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    // IUnitOfWork — used exclusively by the TransactionBehavior.
    public bool HasActiveTransaction => Database.CurrentTransaction is not null;

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        Database.BeginTransactionAsync(cancellationToken);

    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<MatchPlayer> MatchPlayers => Set<MatchPlayer>();
    public DbSet<ShareToken> ShareTokens => Set<ShareToken>();
    public DbSet<TeamMembership> TeamMemberships => Set<TeamMembership>();
    public DbSet<Season> Seasons => Set<Season>();
    public DbSet<SeasonPlayer> SeasonPlayers => Set<SeasonPlayer>();
    public DbSet<SeasonPlayerResult> SeasonPlayerResults => Set<SeasonPlayerResult>();
    public DbSet<SeasonPair> SeasonPairs => Set<SeasonPair>();
    public DbSet<SeasonAward> SeasonAwards => Set<SeasonAward>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ChatMessageReaction> ChatMessageReactions => Set<ChatMessageReaction>();
    public DbSet<ChatReadState> ChatReadStates => Set<ChatReadState>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<LadderLeader> LadderLeaders => Set<LadderLeader>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Team configuration
        modelBuilder.Entity<Team>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.CodeName).IsRequired().HasMaxLength(50);
            entity.Property(e => e.PasswordHash).IsRequired().HasMaxLength(256);
            entity.HasIndex(e => e.CodeName).IsUnique();

            entity.HasOne(e => e.CaptainUser)
                .WithMany(u => u.CaptainedTeams)
                .HasForeignKey(e => e.CaptainUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Player configuration
        modelBuilder.Entity<Player>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(50);
            entity.Property(e => e.AvatarId).IsRequired();
            entity.Property(e => e.Elo).IsRequired().HasDefaultValue(1000);
            entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
            entity.HasIndex(e => e.TeamId);

            entity.HasOne(e => e.Team)
                .WithMany(t => t.Players)
                .HasForeignKey(e => e.TeamId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany(u => u.Players)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.SetNull);

            // At most one user-Player per team (placeholders allowed any number)
            entity.HasIndex(e => new { e.TeamId, e.UserId })
                .IsUnique()
                .HasFilter("[UserId] IS NOT NULL");
        });

        // Match configuration
        modelBuilder.Entity<Match>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Team1Score).IsRequired();
            entity.Property(e => e.Team2Score).IsRequired();
            entity.HasIndex(e => new { e.TeamId, e.PlayedAt }).IsDescending(false, true);
            entity.HasIndex(e => e.SeasonId);

            // Optimistic guard: a match delete reading SeasonId == null takes no season lock, so a
            // season-create import committing mid-delete would otherwise lose its assignment (and
            // the ladder would keep a deleted match). With the token, the losing write fails with
            // DbUpdateConcurrencyException and rolls back instead.
            entity.Property(e => e.SeasonId).IsConcurrencyToken();

            entity.HasOne(e => e.Team)
                .WithMany(t => t.Matches)
                .HasForeignKey(e => e.TeamId)
                .OnDelete(DeleteBehavior.Cascade);

            // ON DELETE NO ACTION: SET NULL would create a second cascade path onto Match
            // (Team→Match plus Team→Season→Match) which SQL Server rejects. DeleteSeasonCommand
            // nulls Match.SeasonId explicitly inside its transaction.
            entity.HasOne(e => e.Season)
                .WithMany(s => s.Matches)
                .HasForeignKey(e => e.SeasonId)
                .OnDelete(DeleteBehavior.ClientSetNull);
        });

        // MatchPlayer configuration
        modelBuilder.Entity<MatchPlayer>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TeamNumber).IsRequired();
            entity.Property(e => e.Position).IsRequired().HasMaxLength(10);
            entity.Property(e => e.EloChange).IsRequired();
            entity.Property(e => e.EloBefore).IsRequired();
            entity.Property(e => e.EloAfter).IsRequired();
            entity.HasIndex(e => e.MatchId);
            entity.HasIndex(e => e.PlayerId);

            entity.HasOne(e => e.Match)
                .WithMany(m => m.MatchPlayers)
                .HasForeignKey(e => e.MatchId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Player)
                .WithMany(p => p.MatchPlayers)
                .HasForeignKey(e => e.PlayerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ShareToken configuration
        modelBuilder.Entity<ShareToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Token).IsRequired().HasMaxLength(64);
            entity.HasIndex(e => e.Token).IsUnique();
            entity.HasIndex(e => e.ExpiresAt);

            entity.HasOne(e => e.Team)
                .WithMany(t => t.ShareTokens)
                .HasForeignKey(e => e.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Season configuration
        modelBuilder.Entity<Season>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.HasIndex(e => e.TeamId);
            // Supports the lazy-close guard query (ClosedAt == null && EndsAt <= now) per team.
            entity.HasIndex(e => new { e.TeamId, e.ClosedAt, e.EndsAt });
            // The announcement lookup filters on different columns than the due-close one
            // (StartAnnouncedAt == null && StartsAt <= now), so it needs its own index.
            entity.HasIndex(e => new { e.TeamId, e.StartAnnouncedAt, e.StartsAt });

            entity.HasOne(e => e.Team)
                .WithMany(t => t.Seasons)
                .HasForeignKey(e => e.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // SeasonPlayer configuration (live ladder row)
        modelBuilder.Entity<SeasonPlayer>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Elo).IsRequired().HasDefaultValue(Constants.Elo.DefaultRating);
            entity.HasIndex(e => e.SeasonId);
            entity.HasIndex(e => new { e.SeasonId, e.PlayerId }).IsUnique();

            entity.HasOne(e => e.Season)
                .WithMany(s => s.SeasonPlayers)
                .HasForeignKey(e => e.SeasonId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Player)
                .WithMany()
                .HasForeignKey(e => e.PlayerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // SeasonPlayerResult configuration (frozen results, insert-only; PK = FK to the ladder row)
        modelBuilder.Entity<SeasonPlayerResult>(entity =>
        {
            entity.HasKey(e => e.SeasonPlayerId);

            entity.HasOne(e => e.SeasonPlayer)
                .WithOne(sp => sp.Result)
                .HasForeignKey<SeasonPlayerResult>(e => e.SeasonPlayerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // SeasonPair configuration (frozen pair standings)
        modelBuilder.Entity<SeasonPair>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.SeasonId);
            entity.HasIndex(e => new { e.SeasonId, e.Player1Id, e.Player2Id }).IsUnique();

            entity.HasOne(e => e.Season)
                .WithMany(s => s.Pairs)
                .HasForeignKey(e => e.SeasonId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Player1)
                .WithMany()
                .HasForeignKey(e => e.Player1Id)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Player2)
                .WithMany()
                .HasForeignKey(e => e.Player2Id)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // SeasonAward configuration (permanent achievements)
        modelBuilder.Entity<SeasonAward>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Category).IsRequired().HasMaxLength(20);
            entity.HasIndex(e => e.SeasonId);
            entity.HasIndex(e => e.PlayerId);
            // Backstop against duplicate award generation.
            entity.HasIndex(e => new { e.SeasonId, e.Category, e.Rank, e.PlayerId }).IsUnique();

            entity.HasOne(e => e.Season)
                .WithMany(s => s.Awards)
                .HasForeignKey(e => e.SeasonId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Player)
                .WithMany()
                .HasForeignKey(e => e.PlayerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.PartnerPlayer)
                .WithMany()
                .HasForeignKey(e => e.PartnerPlayerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ChatMessage configuration
        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Body).IsRequired().HasMaxLength(Constants.Chat.MaxMessageLength);
            // Serves history pagination and the unread count (both filter on TeamId + Id).
            entity.HasIndex(e => new { e.TeamId, e.Id });
            // Serves the once-per-panel-open join-floor lookup, which filters on CreatedAt.
            entity.HasIndex(e => new { e.TeamId, e.CreatedAt });

            entity.HasOne(e => e.Team)
                .WithMany()
                .HasForeignKey(e => e.TeamId)
                .OnDelete(DeleteBehavior.Cascade);

            // Restrict, matching the repo's user/player FK convention: there is no
            // user-deletion path today, and keeping user FKs non-cascading avoids a future
            // cascade diamond (AppUser→ChatMessage alongside Team→ChatMessage→Reaction).
            entity.HasOne(e => e.Sender)
                .WithMany()
                .HasForeignKey(e => e.SenderUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ChatMessageReaction configuration
        modelBuilder.Entity<ChatMessageReaction>(entity =>
        {
            entity.HasKey(e => e.Id);
            // Binary collation: on the SQL Server default (CI_AS) collation,
            // supplementary-plane characters — i.e. most emoji — have undefined collation
            // weights and compare equal (N'😀' = N'😂'), which would make the unique index
            // below treat any two emoji from one user as duplicates and let the toggle-off
            // lookup match the wrong row.
            entity.Property(e => e.Emoji)
                .IsRequired()
                .HasMaxLength(Constants.Chat.MaxReactionEmojiLength)
                .UseCollation("Latin1_General_100_BIN2");
            // One of each emoji per user per message (adding an existing one toggles off).
            // Its (MessageId) prefix also serves the load-by-message query.
            entity.HasIndex(e => new { e.MessageId, e.UserId, e.Emoji }).IsUnique();

            entity.HasOne(e => e.Message)
                .WithMany(m => m.Reactions)
                .HasForeignKey(e => e.MessageId)
                .OnDelete(DeleteBehavior.Cascade);

            // Restrict — consistent with ChatMessage.SenderUserId; reactions still
            // cascade-delete via MessageId, keeping a single cascade path into this table.
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ChatReadState configuration
        modelBuilder.Entity<ChatReadState>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.TeamId }).IsUnique();

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Team)
                .WithMany()
                .HasForeignKey(e => e.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Notification configuration (per-recipient feed row)
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Category).HasMaxLength(16);
            entity.Property(e => e.Emoji).HasMaxLength(Constants.Chat.MaxReactionEmojiLength);
            entity.Property(e => e.DedupKey).IsRequired().HasMaxLength(128);

            // The feed page and its keyset cursor.
            entity.HasIndex(e => new { e.UserId, e.Id }).IsDescending(false, true);
            // The badge count (runs on every bell render) and Home's per-team breakdown. ReadAt
            // needs no index: it is only ever read on rows the feed has already loaded.
            entity.HasIndex(e => e.UserId)
                .HasFilter("[SeenAt] IS NULL")
                .IncludeProperties(e => e.TeamId)
                .HasDatabaseName("IX_Notifications_UserId_Unseen");
            // Idempotency backstop (AI/notifications.md §4.3).
            entity.HasIndex(e => new { e.UserId, e.DedupKey }).IsUnique();
            // The two hard-delete cleanups and the chat read-sync.
            entity.HasIndex(e => e.MatchId);
            entity.HasIndex(e => e.SeasonId);
            entity.HasIndex(e => e.ChatMessageId);

            // Restrict, matching the repo's convention for CONTENT-bearing user FKs
            // (ChatMessage.SenderUserId, ChatMessageReaction.UserId); per-user STATE rows like
            // ChatReadState cascade instead, which is what NotificationPreference mirrors.
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // A deleted team takes its notifications, like its chat.
            entity.HasOne(e => e.Team)
                .WithMany()
                .HasForeignKey(e => e.TeamId)
                .OnDelete(DeleteBehavior.Cascade);

            // Every subject FK is Restrict: TeamId already cascades, and Team → Match →
            // Notification alongside Team → Notification would be two delete paths from one root,
            // which SQL Server rejects (the hazard ChatMessage documents). Restrict emits the same
            // NO ACTION constraint AND stops EF's change tracker from quietly fixing things up.
            // Consequence: DeleteMatchCommand and DeleteSeasonCommand delete their rows explicitly.
            entity.HasOne(e => e.ActorPlayer)
                .WithMany()
                .HasForeignKey(e => e.ActorPlayerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.SubjectPlayer)
                .WithMany()
                .HasForeignKey(e => e.SubjectPlayerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Match)
                .WithMany()
                .HasForeignKey(e => e.MatchId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Season)
                .WithMany()
                .HasForeignKey(e => e.SeasonId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.ChatMessage)
                .WithMany()
                .HasForeignKey(e => e.ChatMessageId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // NotificationPreference configuration (sparse per-team overrides)
        modelBuilder.Entity<NotificationPreference>(entity =>
        {
            entity.HasKey(e => e.Id);
            // SQL Server's unique index permits a single NULL, so the reserved global-defaults
            // tier (TeamId == null) fits without changing this. HasFilter(null) is required:
            // EF's default for a unique index over a nullable column is a
            // "WHERE [TeamId] IS NOT NULL" filter, which would leave that tier unconstrained.
            entity.HasIndex(e => new { e.UserId, e.TeamId, e.Category }).IsUnique().HasFilter(null);
            entity.HasIndex(e => e.UserId);

            // Two cascade FKs from DIFFERENT roots is fine and already precedented by
            // ChatReadState / TeamMembership. The rejected pattern is two paths from ONE root.
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Team)
                .WithMany()
                .HasForeignKey(e => e.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // LadderLeader configuration (current #1 snapshot per team, scope and category)
        modelBuilder.Entity<LadderLeader>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Category).IsRequired().HasMaxLength(16);
            // Exactly one row per (team, scope, category); the single permitted NULL SeasonId is
            // the all-time scope. HasFilter(null) is load-bearing: EF's default for a unique index
            // over a nullable column is a "WHERE [SeasonId] IS NOT NULL" filter, which would leave
            // the all-time rows — the ones this must constrain most — entirely unconstrained.
            entity.HasIndex(e => new { e.TeamId, e.SeasonId, e.Category }).IsUnique().HasFilter(null);

            entity.HasOne(e => e.Team)
                .WithMany()
                .HasForeignKey(e => e.TeamId)
                .OnDelete(DeleteBehavior.Cascade);

            // Cascade-diamond again (Team → Season → LadderLeader alongside Team → LadderLeader):
            // DeleteSeasonCommand clears these rows explicitly.
            entity.HasOne(e => e.Season)
                .WithMany()
                .HasForeignKey(e => e.SeasonId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Player)
                .WithMany()
                .HasForeignKey(e => e.PlayerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.PartnerPlayer)
                .WithMany()
                .HasForeignKey(e => e.PartnerPlayerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // TeamMembership configuration
        modelBuilder.Entity<TeamMembership>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.TeamId }).IsUnique();
            entity.HasIndex(e => e.TeamId);

            entity.HasOne(e => e.User)
                .WithMany(u => u.Memberships)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Team)
                .WithMany(t => t.Members)
                .HasForeignKey(e => e.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
