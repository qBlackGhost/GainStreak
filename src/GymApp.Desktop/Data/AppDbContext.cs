using System;
using System.IO;
using GymApp.Desktop.Models;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Desktop.Data;

public class AppDbContext : DbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<WorkoutSplit> WorkoutSplits => Set<WorkoutSplit>();
    public DbSet<SplitDay> SplitDays => Set<SplitDay>();
    public DbSet<Gym> Gyms => Set<Gym>();
    public DbSet<WorkoutLog> WorkoutLogs => Set<WorkoutLog>();
    public DbSet<CalorieLog> CalorieLogs => Set<CalorieLog>();
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

        b.Entity<WorkoutLog>().HasOne(w => w.Gym).WithMany()
            .HasForeignKey(w => w.GymId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<WorkoutLog>().HasOne(w => w.SplitDay).WithMany()
            .HasForeignKey(w => w.SplitDayId).OnDelete(DeleteBehavior.SetNull);

        // One workout entry and one calorie entry per user per day
        b.Entity<WorkoutLog>().HasIndex(w => new { w.UserId, w.Date }).IsUnique();
        b.Entity<CalorieLog>().HasIndex(c => new { c.UserId, c.Date }).IsUnique();

        SeedSplits(b);
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
}