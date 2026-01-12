using Fotbalek.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fotbalek.Web.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<MatchPlayer> MatchPlayers => Set<MatchPlayer>();
    public DbSet<ShareToken> ShareTokens => Set<ShareToken>();

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
        });

        // Match configuration
        modelBuilder.Entity<Match>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Team1Score).IsRequired();
            entity.Property(e => e.Team2Score).IsRequired();
            entity.HasIndex(e => new { e.TeamId, e.PlayedAt }).IsDescending(false, true);

            entity.HasOne(e => e.Team)
                .WithMany(t => t.Matches)
                .HasForeignKey(e => e.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
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
    }
}
