using System;
using System.IO;
using GymApp.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Data;

public class AppDbContext : DbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<WorkoutSplit> WorkoutSplits => Set<WorkoutSplit>();
    public DbSet<SplitDay> SplitDays => Set<SplitDay>();
    public DbSet<SplitVariant> SplitVariants => Set<SplitVariant>();
    public DbSet<Gym> Gyms => Set<Gym>();
    public DbSet<WorkoutLog> WorkoutLogs => Set<WorkoutLog>();
    public DbSet<CalorieLog> CalorieLogs => Set<CalorieLog>();
    public DbSet<UserSchedule> UserSchedules => Set<UserSchedule>();
    public AppDbContext() { }
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        if (options.IsConfigured) return;
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GymApp");
        Directory.CreateDirectory(dir);
        options.UseSqlite($"Data Source={Path.Combine(dir, "gymapp.db")}");
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasIndex(u => u.Username).IsUnique();
        b.Entity<User>().Property(u => u.Username).HasMaxLength(32).UseCollation("NOCASE");
        b.Entity<User>().Property(u => u.GoalType).HasConversion<string>();

        // Two separate relationships between User and WorkoutSplit, so be explicit
        b.Entity<User>().HasOne(u => u.SelectedSplit).WithMany()
            .HasForeignKey(u => u.SelectedSplitId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<WorkoutSplit>().HasOne(s => s.Owner).WithMany()
            .HasForeignKey(s => s.OwnerUserId).OnDelete(DeleteBehavior.Cascade);

        // Preset layouts (e.g. Full Body 2 / 3 days) and the user's chosen one
        b.Entity<SplitVariant>().HasOne(v => v.Split).WithMany(s => s.Variants)
            .HasForeignKey(v => v.SplitId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<SplitVariant>().Property(v => v.Name).HasMaxLength(40);
        b.Entity<SplitVariant>().Property(v => v.Pattern).HasMaxLength(7);
        b.Entity<User>().HasOne(u => u.SelectedVariant).WithMany()
            .HasForeignKey(u => u.SelectedVariantId).OnDelete(DeleteBehavior.SetNull);

        b.Entity<WorkoutLog>().HasOne(w => w.Gym).WithMany()
            .HasForeignKey(w => w.GymId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<WorkoutLog>().HasOne(w => w.SplitDay).WithMany()
            .HasForeignKey(w => w.SplitDayId).OnDelete(DeleteBehavior.SetNull);

        // One workout entry and one calorie entry per user per day
        b.Entity<WorkoutLog>().HasIndex(w => new { w.UserId, w.Date }).IsUnique();
        b.Entity<CalorieLog>().HasIndex(c => new { c.UserId, c.Date }).IsUnique();
        
        b.Entity<UserSchedule>().HasIndex(s => new { s.UserId, s.EffectiveFrom }).IsUnique();
        b.Entity<UserSchedule>().Property(s => s.WeekPattern).HasMaxLength(7);
        b.Entity<UserSchedule>().HasOne(s => s.User).WithMany()
            .HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<UserSchedule>().HasOne(s => s.Split).WithMany()
            .HasForeignKey(s => s.SplitId).OnDelete(DeleteBehavior.SetNull);

        SeedSplits(b);
        SeedVariants(b);
    }

    private static void SeedSplits(ModelBuilder b)
    {
        b.Entity<WorkoutSplit>().HasData(
            new WorkoutSplit { Id = 1, Name = "Push / Pull / Legs", IsRecommended = true },
            new WorkoutSplit { Id = 2, Name = "Upper / Lower", IsRecommended = true },
            new WorkoutSplit { Id = 3, Name = "PPL + Upper / Lower", IsRecommended = true },
            new WorkoutSplit { Id = 4, Name = "Full Body" },
            new WorkoutSplit { Id = 5, Name = "Bro Split" });

        b.Entity<SplitDay>().HasData(
            //PPL
            new SplitDay { Id = 1, SplitId = 1, Order = 1, Name = "Push" },
            new SplitDay { Id = 2, SplitId = 1, Order = 2, Name = "Pull" },
            new SplitDay { Id = 3, SplitId = 1, Order = 3, Name = "Legs" },
            //Upper/Lower
            new SplitDay { Id = 4, SplitId = 2, Order = 1, Name = "Upper" },
            new SplitDay { Id = 5, SplitId = 2, Order = 2, Name = "Lower" },
            //PPL + Upper/Lower
            new SplitDay { Id = 6, SplitId = 3, Order = 1, Name = "Push" },
            new SplitDay { Id = 7, SplitId = 3, Order = 2, Name = "Pull" },
            new SplitDay { Id = 8, SplitId = 3, Order = 3, Name = "Legs" },
            new SplitDay { Id = 9, SplitId = 3, Order = 4, Name = "Upper" },
            new SplitDay { Id = 10, SplitId = 3, Order = 5, Name = "Lower" },
            //Full Body
            new SplitDay { Id = 11, SplitId = 4, Order = 1, Name = "Full Body" },
            //Bro Split
            new SplitDay { Id = 12, SplitId = 5, Order = 1, Name = "Chest" },
            new SplitDay { Id = 13, SplitId = 5, Order = 2, Name = "Back" },
            new SplitDay { Id = 14, SplitId = 5, Order = 3, Name = "Shoulders" },
            new SplitDay { Id = 15, SplitId = 5, Order = 4, Name = "Legs" },
            new SplitDay { Id = 16, SplitId = 5, Order = 5, Name = "Arms" });
    }

    // Pattern: 7 days starting on the day the user picks. W = workout, R = rest.
    private static void SeedVariants(ModelBuilder b)
    {
        b.Entity<SplitVariant>().HasData(
            // PPL: 3 workout, 1 rest, 3 workout
            new SplitVariant { Id = 1, SplitId = 1, Name = "6 days / week", Pattern = "WWWRWWW", IsDefault = true },
            // Upper/Lower: 2 workout, 1 rest, 2 workout, 2 rest
            new SplitVariant { Id = 2, SplitId = 2, Name = "4 days / week", Pattern = "WWRWWRR", IsDefault = true },
            // PPL + Upper/Lower: 3 workout, 1 rest, 2 workout, 1 rest
            new SplitVariant { Id = 3, SplitId = 3, Name = "5 days / week", Pattern = "WWWRWWR", IsDefault = true },
            // Full Body: 2 days (W, 2 rest, W, 3 rest) or 3 days (W, R, W, R, W, 2 rest)
            new SplitVariant { Id = 4, SplitId = 4, Name = "2 days / week", Pattern = "WRRWRRR" },
            new SplitVariant { Id = 5, SplitId = 4, Name = "3 days / week", Pattern = "WRWRWRR", IsDefault = true },
            // Bro Split: 5 workout, 2 rest
            new SplitVariant { Id = 6, SplitId = 5, Name = "5 days / week", Pattern = "WWWWWRR", IsDefault = true });
    }
}
