using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Tests;

public sealed class TestDb : IDbContextFactory<AppDbContext>, IDisposable
{
    private readonly SqliteConnection? _conn;
    private readonly string? _path;
    private readonly DbContextOptions<AppDbContext>? _options;
    private readonly IDbContextFactory<AppDbContext>? _wrapped;

    /// <summary>An in-memory database on one shared connection: fast, but for one thread at a time.</summary>
    public TestDb() : this(onDisk: false) { }

    private TestDb(bool onDisk)
    {
        if (onDisk)
        {
            // A file, with a connection per context like the app: safe for code that writes from background tasks
            _path = Path.Combine(Path.GetTempPath(), $"gymapp-test-{Guid.NewGuid():N}.db");
            _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_path};Pooling=False").Options;
        }
        else
        {
            _conn = new SqliteConnection("Data Source=:memory:");
            _conn.Open();   // the in-memory database lives as long as this connection
            _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        }
        using var db = CreateDbContext();
        db.Database.Migrate();
    }

    /// <summary>The seeding helpers over someone else's database (e.g. the app's own factory); it isn't owned or migrated.</summary>
    public TestDb(IDbContextFactory<AppDbContext> wrapped) => _wrapped = wrapped;

    /// <summary>A database in a temporary file, for tests where several threads use it at once.</summary>
    public static TestDb OnDisk() => new(onDisk: true);

    public AppDbContext CreateDbContext() => _wrapped?.CreateDbContext() ?? new(_options!);

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

    public void Dispose()
    {
        _conn?.Dispose();
        if (_path is null) return;
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
            if (File.Exists(file)) File.Delete(file);
    }
}