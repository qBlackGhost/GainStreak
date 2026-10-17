using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Tests;

public sealed class TestDb : IDbContextFactory<AppDbContext>, IDisposable
{
    private readonly SqliteConnection _conn = new("Data Source=:memory:");
    private readonly DbContextOptions<AppDbContext> _options;

    public TestDb()
    {
        _conn.Open();   // the in-memory database lives as long as this connection
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        using var db = CreateDbContext();
        db.Database.Migrate();
    }

    public AppDbContext CreateDbContext() => new(_options);

    public int AddUser(string username)
    {
        using var db = CreateDbContext();
        var user = new User { Username = username, PasswordHash = "x" };
        db.Users.Add(user);
        db.SaveChanges();
        return user.Id;
    }

    /// <summary>Gives the user a custom split with one day per given weekday and selects it. Returns the day ids in order.</summary>
    public List<int> AddSplit(int userId, params (string Name, DayOfWeek Weekday)[] days)
    {
        using var db = CreateDbContext();
        var split = new WorkoutSplit { Name = "Test split", OwnerUserId = userId };
        for (var i = 0; i < days.Length; i++)
            split.Days.Add(new SplitDay { Name = days[i].Name, Order = i + 1, Weekday = days[i].Weekday });
        db.WorkoutSplits.Add(split);
        db.SaveChanges();
        db.Users.Single(u => u.Id == userId).SelectedSplitId = split.Id;
        db.SaveChanges();
        return split.Days.Select(d => d.Id).ToList();
    }

    /// <summary>Adds <paramref name="count"/> built-in exercises to the day, each with the given sets. Returns their ids in order.</summary>
    public List<int> AddDayExercises(int userId, int splitDayId, int count, int warmupSets = 0, int workingSets = 3)
    {
        using var db = CreateDbContext();
        var ids = db.Exercises.Where(e => e.OwnerUserId == null).OrderBy(e => e.Id).Select(e => e.Id).Take(count).ToList();
        for (var i = 0; i < ids.Count; i++)
            db.UserDayExercises.Add(new UserDayExercise
            {
                UserId = userId, SplitDayId = splitDayId, ExerciseId = ids[i], Order = i + 1,
                WarmupSets = warmupSets, WorkingSets = workingSets
            });
        db.SaveChanges();
        return ids;
    }

    /// <summary>Sets the user up so "I went today" passes the check-in rule on <paramref name="date"/>: a split day on
    /// that weekday with one exercise of one working set, and that set logged.</summary>
    public void ReadyToCheckIn(int userId, DateOnly date)
    {
        var day = AddSplit(userId, ("Full body", date.DayOfWeek))[0];
        var exercise = AddDayExercises(userId, day, 1, workingSets: 1)[0];
        using var db = CreateDbContext();
        db.SetLogs.Add(new SetLog
            { UserId = userId, ExerciseId = exercise, Date = date, SetNumber = 1, Kind = SetKind.Working, Reps = 8, WeightKg = 50 });
        db.SaveChanges();
    }

    public void Dispose() => _conn.Dispose();
}