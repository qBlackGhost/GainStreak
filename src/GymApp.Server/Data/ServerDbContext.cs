using GymApp.Contracts;
using GymApp.Core;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Server.Data;

/// <summary>The server's Postgres database: accounts, sessions, friendships, projected stats, shared check-ins, feedback
/// and the splits and workouts friends share.
/// Separate from the app's AppDbContext, which never leaves the phone.</summary>
public class ServerDbContext(DbContextOptions<ServerDbContext> options) : DbContext(options)
{
    public const string UsernameIndex = "IX_Users_UsernameNormalized";
    public const string EmailIndex = "IX_Users_Email";

    public DbSet<User> Users => Set<User>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<StatsDay> ProjectedStats => Set<StatsDay>();
    public DbSet<CheckIn> CheckIns => Set<CheckIn>();
    public DbSet<FeedbackEntry> Feedback => Set<FeedbackEntry>();
    public DbSet<Share> Shares => Set<Share>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.Property(u => u.Username).HasMaxLength(Credentials.MaxUsernameLength);
            e.Property(u => u.UsernameNormalized).HasMaxLength(Credentials.MaxUsernameLength);
            e.Property(u => u.Email).HasMaxLength(Credentials.MaxEmailLength);
            e.HasIndex(u => u.UsernameNormalized).IsUnique().HasDatabaseName(UsernameIndex);
            e.HasIndex(u => u.Email).IsUnique().HasDatabaseName(EmailIndex);
        });

        b.Entity<Session>(e =>
        {
            e.Property(s => s.TokenHash).HasMaxLength(64);
            e.Property(s => s.DeviceName).HasMaxLength(Session.MaxDeviceNameLength);
            e.HasIndex(s => s.TokenHash).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Friendship>(e =>
        {
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Friendships_Order", "\"UserAId\" < \"UserBId\"");
                t.HasCheckConstraint("CK_Friendships_Requester", "\"RequestedById\" IN (\"UserAId\", \"UserBId\")");
            });
            e.Property(f => f.Status).HasConversion<string>().HasMaxLength(16);   // by name, like the app
            e.HasIndex(f => new { f.UserAId, f.UserBId }).IsUnique();
            e.HasIndex(f => f.UserBId);
            e.HasOne<User>().WithMany().HasForeignKey(f => f.UserAId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(f => f.UserBId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<StatsDay>(e =>
        {
            e.ToTable("ProjectedStats");
            e.HasKey(d => new { d.UserId, d.Date });
            e.HasOne<User>().WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CheckIn>(e =>
        {
            e.HasKey(c => new { c.UserId, c.Date });
            e.HasIndex(c => c.Date);   // the daily cleanup
            e.Property(c => c.GymOsmType).HasConversion<string>().HasMaxLength(16);
            e.Property(c => c.CityOsmType).HasConversion<string>().HasMaxLength(16);
            e.Property(c => c.GymName).HasMaxLength(CheckIn.MaxNameLength);
            e.Property(c => c.CityName).HasMaxLength(CheckIn.MaxNameLength);
            e.ToTable(t => t.HasCheckConstraint("CK_CheckIns_CustomGymHasNoPosition",
                "\"GymOsmId\" IS NOT NULL OR (\"Latitude\" IS NULL AND \"Longitude\" IS NULL)"));
            e.HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<FeedbackEntry>(e =>
        {
            e.ToTable("Feedback");
            e.Property(f => f.Kind).HasConversion<string>().HasMaxLength(16);
            e.Property(f => f.Text).HasMaxLength(FeedbackRules.MaxTextLength);
            e.Property(f => f.Report).HasMaxLength(FeedbackRules.MaxReportLength);
            e.Property(f => f.AppVersion).HasMaxLength(32);
            e.HasIndex(f => f.CreatedAt);
            // Deleting an account deletes what it sent
            e.HasOne<User>().WithMany().HasForeignKey(f => f.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Share>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Payload).HasMaxLength(Share.MaxPayloadLength);
            e.HasIndex(x => new { x.ToUserId, x.CreatedAt });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.FromUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ToUserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
