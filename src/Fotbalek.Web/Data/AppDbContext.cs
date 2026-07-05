using Fotbalek.Web.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Web.Data;

public class AppDbContext : IdentityDbContext<AppUser, IdentityRole<int>, int>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

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

            entity.HasOne(e => e.AdminUser)
                .WithMany(u => u.AdministeredTeams)
                .HasForeignKey(e => e.AdminUserId)
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
            // (Team→Match plus Team→Season→Match) which SQL Server rejects. SeasonService.DeleteAsync
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
