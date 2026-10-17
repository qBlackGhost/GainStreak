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
    public DbSet<Exercise> Exercises => Set<Exercise>();
    public DbSet<UserDayExercise> UserDayExercises => Set<UserDayExercise>();
    public DbSet<SetLog> SetLogs => Set<SetLog>();
    public DbSet<PrAdjustment> PrAdjustments => Set<PrAdjustment>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<WeightLog> WeightLogs => Set<WeightLog>();
    public DbSet<PointsEntry> PointsEntries => Set<PointsEntry>();
    public DbSet<City> Cities => Set<City>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public AppDbContext() { }
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    /// <summary>Where the database and remember-me file live. GYMAPP_DATA_DIR overrides it, so a test run
    /// can use a copy of the data instead of the real one.</summary>
    public static string DataDirectory =>
        Environment.GetEnvironmentVariable("GYMAPP_DATA_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GymApp");

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        if (options.IsConfigured) return;
        var dir = DataDirectory;
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

        // A gym a log points to is deactivated, never deleted; Restrict makes a mistake fail loudly
        b.Entity<WorkoutLog>().HasOne(w => w.Gym).WithMany()
            .HasForeignKey(w => w.GymId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WorkoutLog>().HasOne(w => w.SplitDay).WithMany()
            .HasForeignKey(w => w.SplitDayId).OnDelete(DeleteBehavior.SetNull);

        // One workout entry and one calorie entry per user per day
        b.Entity<WorkoutLog>().HasIndex(w => new { w.UserId, w.Date }).IsUnique();
        b.Entity<CalorieLog>().HasIndex(c => new { c.UserId, c.Date }).IsUnique();
        b.Entity<CalorieLog>().Property(c => c.GoalTypeAtTime).HasConversion<string>();
        
        b.Entity<UserSchedule>().HasIndex(s => new { s.UserId, s.EffectiveFrom }).IsUnique();
        b.Entity<UserSchedule>().Property(s => s.WeekPattern).HasMaxLength(7);
        b.Entity<UserSchedule>().HasOne(s => s.User).WithMany()
            .HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<UserSchedule>().HasOne(s => s.Split).WithMany()
            .HasForeignKey(s => s.SplitId).OnDelete(DeleteBehavior.SetNull);

        // Exercises: names are unique per owner, case-insensitive
        b.Entity<Exercise>().Property(e => e.Name).HasMaxLength(60).UseCollation("NOCASE");
        b.Entity<Exercise>().Property(e => e.MuscleGroup).HasConversion<string>();
        b.Entity<Exercise>().HasIndex(e => new { e.OwnerUserId, e.Name }).IsUnique();
        b.Entity<Exercise>().HasOne(e => e.Owner).WithMany()
            .HasForeignKey(e => e.OwnerUserId).OnDelete(DeleteBehavior.Cascade);

// A user's exercise list per split day
        b.Entity<UserDayExercise>().HasIndex(x => new { x.UserId, x.SplitDayId, x.ExerciseId }).IsUnique();
        b.Entity<UserDayExercise>().HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<UserDayExercise>().HasOne(x => x.SplitDay).WithMany()
            .HasForeignKey(x => x.SplitDayId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<UserDayExercise>().HasOne(x => x.Exercise).WithMany()
            .HasForeignKey(x => x.ExerciseId).OnDelete(DeleteBehavior.Cascade);

// Sets: one row per user, exercise, day and set number
        b.Entity<SetLog>().Property(s => s.Kind).HasConversion<string>();
        b.Entity<SetLog>().HasIndex(s => new { s.UserId, s.ExerciseId, s.Date, s.SetNumber }).IsUnique();
        b.Entity<SetLog>().HasOne(s => s.User).WithMany()
            .HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<SetLog>().HasOne(s => s.Exercise).WithMany()
            .HasForeignKey(s => s.ExerciseId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<PrAdjustment>().HasIndex(a => new { a.UserId, a.ExerciseId, a.Date }).IsUnique();
        b.Entity<PrAdjustment>().HasOne(a => a.User).WithMany()
            .HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<PrAdjustment>().HasOne(a => a.Exercise).WithMany()
            .HasForeignKey(a => a.ExerciseId).OnDelete(DeleteBehavior.Cascade);
        
        b.Entity<UserProfile>().HasOne(p => p.User).WithOne()
            .HasForeignKey<UserProfile>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<UserProfile>().Property(p => p.Sex).HasConversion<string>();
        b.Entity<UserProfile>().Property(p => p.Units).HasConversion<string>();
        b.Entity<UserProfile>().Property(p => p.ActivityLevel).HasConversion<string>();

        b.Entity<WeightLog>().HasIndex(w => new { w.UserId, w.Date }).IsUnique();
        b.Entity<WeightLog>().HasOne(w => w.User).WithMany()
            .HasForeignKey(w => w.UserId).OnDelete(DeleteBehavior.Cascade);

        // Points ledger: the unique key stops a day or week from being paid twice, even if the sync has a bug
        b.Entity<PointsEntry>().Property(p => p.Reason).HasConversion<string>();
        b.Entity<PointsEntry>().Property(p => p.DedupKey).HasMaxLength(40);
        b.Entity<PointsEntry>().HasIndex(p => new { p.UserId, p.DedupKey }).IsUnique();
        b.Entity<PointsEntry>().HasIndex(p => new { p.UserId, p.Date });
        b.Entity<PointsEntry>().HasOne(p => p.User).WithMany()
            .HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);

        // Gym map: cities picked from Nominatim, gyms from Overpass plus users' custom gyms
        b.Entity<City>().Property(c => c.OsmType).HasConversion<string>();
        b.Entity<City>().HasIndex(c => new { c.OsmType, c.OsmId }).IsUnique();

        b.Entity<Gym>().Property(g => g.OsmType).HasConversion<string>();
        b.Entity<Gym>().Property(g => g.Name).UseCollation("NOCASE");
        // Existing rows get true; the sentinel matches the C# initializer so an explicit false is still saved
        b.Entity<Gym>().Property(g => g.IsActive).HasDefaultValue(true).HasSentinel(true);
        // One row per OSM gym per city: city boxes overlap (Dumbrăvița lies inside Timișoara's), and each city
        // must list every gym in its box. OSM ids are only unique per type; custom gyms have neither, so filter them out
        b.Entity<Gym>().HasIndex(g => new { g.CityId, g.OsmType, g.OsmId }).IsUnique()
            .HasFilter("\"OsmType\" IS NOT NULL AND \"OsmId\" IS NOT NULL");
        // Custom gym names are unique per user within a city, case-insensitive (OSM gyms can share names)
        b.Entity<Gym>().HasIndex(g => new { g.OwnerUserId, g.CityId, g.Name }).IsUnique()
            .HasFilter("\"OwnerUserId\" IS NOT NULL");
        b.Entity<Gym>().HasOne(g => g.City).WithMany()
            .HasForeignKey(g => g.CityId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Gym>().HasOne(g => g.Owner).WithMany()
            .HasForeignKey(g => g.OwnerUserId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<UserProfile>().HasOne(p => p.HomeCity).WithMany()
            .HasForeignKey(p => p.HomeCityId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<UserProfile>().Property(p => p.AskForGymAfterCheckIn).HasDefaultValue(true).HasSentinel(true);

        // Friends: one row per pair (UserAId < UserBId, kept by the service), so a pair can only exist once.
        // Two relationships to User, so be explicit; deleting either user removes the friendship
        b.Entity<Friendship>().Property(f => f.Status).HasConversion<string>().HasMaxLength(10);
        b.Entity<Friendship>().HasIndex(f => new { f.UserAId, f.UserBId }).IsUnique();
        b.Entity<Friendship>().HasIndex(f => f.UserBId);
        b.Entity<Friendship>().HasOne(f => f.UserA).WithMany()
            .HasForeignKey(f => f.UserAId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Friendship>().HasOne(f => f.UserB).WithMany()
            .HasForeignKey(f => f.UserBId).OnDelete(DeleteBehavior.Cascade);

        SeedExercises(b);
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
    
    private static void SeedExercises(ModelBuilder b)
{
    static Exercise E(int id, string name, MuscleGroup g) => new() { Id = id, Name = name, MuscleGroup = g };

    b.Entity<Exercise>().HasData(
        // Chest
        E(1, "Bench Press", MuscleGroup.Chest), E(2, "Incline Bench Press", MuscleGroup.Chest),
        E(3, "Dumbbell Bench Press", MuscleGroup.Chest), E(4, "Incline Dumbbell Press", MuscleGroup.Chest),
        E(5, "Cable Fly", MuscleGroup.Chest), E(6, "Push-Up", MuscleGroup.Chest),
        E(49, "Smith Press", MuscleGroup.Chest), E(50, "Incline Smith Press", MuscleGroup.Chest),
        E(51, "Wide Grip Dips", MuscleGroup.Chest),
        // Back
        E(7, "Pull-Up", MuscleGroup.Back), E(8, "Lat Pulldown", MuscleGroup.Back),
        E(9, "Barbell Row", MuscleGroup.Back), E(10, "Seated Cable Row", MuscleGroup.Back),
        E(11, "One-Arm Dumbbell Row", MuscleGroup.Back), E(12, "Deadlift", MuscleGroup.Back),
        E(52, "T-Bar Row", MuscleGroup.Back), E(53, "Low Row", MuscleGroup.Back),
        E(54, "Close Grip Cable Row", MuscleGroup.Back), E(55, "Wide Grip Cable Row", MuscleGroup.Back),

        // Shoulders
        E(13, "Overhead Press", MuscleGroup.Shoulders), E(14, "Dumbbell Shoulder Press", MuscleGroup.Shoulders),
        E(15, "Cable Lateral Raise", MuscleGroup.Shoulders), E(16, "Rear Delt Fly", MuscleGroup.Shoulders),
        E(17, "Face Pull", MuscleGroup.Shoulders), E(56, "Dumbbell Lateral Raise", MuscleGroup.Shoulders),
        // Biceps
        E(18, "Barbell Curl", MuscleGroup.Biceps), E(19, "Dumbbell Curl", MuscleGroup.Biceps),
        E(20, "Hammer Curl", MuscleGroup.Biceps), E(21, "Preacher Curl", MuscleGroup.Biceps),
        E(57, "Preacher Hammer Curl", MuscleGroup.Biceps), E(58, "Incline Dumbbell Curl", MuscleGroup.Biceps),
        // Triceps
        E(22, "Triceps Pushdown", MuscleGroup.Triceps), E(23, "Skull Crusher", MuscleGroup.Triceps),
        E(24, "Overhead Triceps Extension", MuscleGroup.Triceps), E(25, "JM Bench Press", MuscleGroup.Triceps),
        E(26, "Close Grip Dips", MuscleGroup.Triceps), E(59, "One Arm Cable Pushdown", MuscleGroup.Triceps),
        // Quads
        E(27, "Squat", MuscleGroup.Quads), E(28, "Leg Press", MuscleGroup.Quads),
        E(29, "Leg Extension", MuscleGroup.Quads), E(30, "Bulgarian Split Squat", MuscleGroup.Quads),
        E(31, "Front Squat", MuscleGroup.Quads), E(60, "Hack Squat", MuscleGroup.Quads),
        // Hamstrings
        E(32, "Romanian Deadlift", MuscleGroup.Hamstrings), E(33, "Lying Leg Curl", MuscleGroup.Hamstrings),
        E(34, "Seated Leg Curl", MuscleGroup.Hamstrings), E(35, "Good Morning", MuscleGroup.Hamstrings),
        // Glutes
        E(36, "Hip Thrust", MuscleGroup.Glutes), E(37, "Glute Bridge", MuscleGroup.Glutes),
        E(38, "Cable Kickback", MuscleGroup.Glutes), E(39, "Walking Lunge", MuscleGroup.Glutes),
        // Calves
        E(40, "Standing Calf Raise", MuscleGroup.Calves), E(41, "Seated Calf Raise", MuscleGroup.Calves),
        E(42, "Leg Press Calf Raise", MuscleGroup.Calves),
        // Core
        E(43, "Plank", MuscleGroup.Core), E(44, "Hanging Leg Raise", MuscleGroup.Core),
        E(45, "Cable Crunch", MuscleGroup.Core), E(46, "Ab Wheel Rollout", MuscleGroup.Core),
        E(47, "Russian Twist", MuscleGroup.Core), E(48, "Decline Crunches", MuscleGroup.Core),
        //Forearm
        E(61, "Forearm Cable Curl", MuscleGroup.Forearms), E(62, "Forearm Cable Extension", MuscleGroup.Forearms),
        E(63, "Reverse Curls", MuscleGroup.Forearms),  E(64, "Wrist Curls", MuscleGroup.Forearms),
        E(65, "Reverse Wrist Curls", MuscleGroup.Forearms));
}
}
