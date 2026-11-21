using GymApp.Contracts;
using GymApp.Core;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Server.Data;

/// <summary>The server's Postgres database: accounts, sessions, friendships, blocks, projected stats, shared check-ins,
/// feedback, user reports, friend invite codes, the splits and workouts friends share, and push tokens.
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
    public DbSet<UserBlock> UserBlocks => Set<UserBlock>();
    public DbSet<UserReport> UserReports => Set<UserReport>();
    public DbSet<EmailCode> EmailCodes => Set<EmailCode>();
    public DbSet<FriendInvite> FriendInvites => Set<FriendInvite>();
    public DbSet<PushDevice> PushDevices => Set<PushDevice>();
    public DbSet<UserBadge> UserBadges => Set<UserBadge>();
    public DbSet<InviteAccept> InviteAccepts => Set<InviteAccept>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.Property(u => u.Username).HasMaxLength(Credentials.MaxUsernameLength);
            e.Property(u => u.UsernameNormalized).HasMaxLength(Credentials.MaxUsernameLength);
            e.Property(u => u.Email).HasMaxLength(Credentials.MaxEmailLength);
            e.Property(u => u.Avatar).HasMaxLength(Core.AvatarChoice.MaxLength);
            e.Property(u => u.FeaturedBadge).HasMaxLength(User.MaxFeaturedBadgeLength);
            e.Property(u => u.ShareBadges).HasDefaultValue(true).HasSentinel(true);
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

        // Badges (Phase 25)
        b.Entity<UserBadge>(e =>
        {
            e.HasKey(x => new { x.UserId, x.BadgeId });
            e.Property(x => x.BadgeId).HasMaxLength(UserBadge.MaxIdLength);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<InviteAccept>(e =>
        {
            e.HasKey(x => new { x.InviterId, x.InviteeId });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.InviterId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.InviteeId).OnDelete(DeleteBehavior.Cascade);
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

        b.Entity<EmailCode>(e =>
        {
            e.Property(c => c.Purpose).HasConversion<string>().HasMaxLength(16);
            e.Property(c => c.CodeHash).HasMaxLength(64);
            e.HasIndex(c => new { c.UserId, c.Purpose }).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<UserBlock>(e =>
        {
            e.HasKey(x => new { x.BlockerId, x.BlockedId });
            e.HasIndex(x => x.BlockedId);
            e.ToTable(t => t.HasCheckConstraint("CK_UserBlocks_NotSelf", "\"BlockerId\" <> \"BlockedId\""));
            e.HasOne<User>().WithMany().HasForeignKey(x => x.BlockerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.BlockedId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<FriendInvite>(e =>
        {
            e.HasKey(x => x.UserId);
            e.Property(x => x.Code).HasMaxLength(Social.FriendInvites.CodeLength);
            e.HasIndex(x => x.Code).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PushDevice>(e =>
        {
            e.Property(d => d.Platform).HasConversion<string>().HasMaxLength(8);
            e.Property(d => d.Token).HasMaxLength(PushRules.MaxTokenLength);
            e.HasIndex(d => d.Token).IsUnique();
            e.HasIndex(d => d.SessionId).IsUnique();
            e.HasIndex(d => d.UserId);
            // Logging out deletes the session, and the token with it
            e.HasOne<Session>().WithMany().HasForeignKey(d => d.SessionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<UserReport>(e =>
        {
            e.Property(r => r.Reason).HasConversion<string>().HasMaxLength(16);
            e.Property(r => r.Note).HasMaxLength(UserReportRules.MaxNoteLength);
            e.HasIndex(r => r.CreatedAt);
            e.HasIndex(r => r.ReportedUserId);
            // Deleting an account deletes what it sent, and a report about an account that's gone has nothing to act on
            e.HasOne<User>().WithMany().HasForeignKey(r => r.ReporterId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(r => r.ReportedUserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
