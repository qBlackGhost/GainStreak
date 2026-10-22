using System.Diagnostics;
using System.Reflection;
using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using GymApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MobileSpike;

/// <summary>The 16a checks, with the real GymApp.Data and GymApp.Services: migrate, splits, a logged workout with its
/// streak and points, and the server's /health. The map is checked by hand on screen.</summary>
public static class SpikeRunner
{
    public static async Task RunAsync(Action<string> onLine)
    {
        void Step(string s) { SpikeLog.Mark(s); onLine(s); }
        void Fail(string step, Exception e) { SpikeLog.Write($"FAIL {step}: {e}"); onLine(e.Message); }

        Step($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}, " +
             $"dynamic code {(System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported ? "supported" : "NOT supported")}, " +
             $"compiled {(System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeCompiled ? "yes" : "no (interpreted)")}");

        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(o => o
            .UseSqlite($"Data Source={Path.Combine(DataDir(), "gymapp.db")}")
            .LogTo(l => SpikeLog.Write("EF " + l), LogLevel.Warning));
        services.AddTransient<PointsService>();
        services.AddTransient<StreakService>();
        services.AddTransient(sp => new SplitService(sp.GetRequiredService<IDbContextFactory<AppDbContext>>(), sp.GetRequiredService<PointsService>()));
        services.AddTransient(sp => new WorkoutService(sp.GetRequiredService<IDbContextFactory<AppDbContext>>(),
            sp.GetRequiredService<StreakService>(), sp.GetRequiredService<PointsService>()));
        services.AddTransient<DayExerciseService>();
        services.AddTransient<SetService>();
        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();

        // 1. Migrate
        try
        {
            var dbPath = Path.Combine(DataDir(), "gymapp.db");
            var fresh = !File.Exists(dbPath);
            Step($"1. DB {dbPath} ({(fresh ? "fresh install" : "already there")})");
            var sw = Stopwatch.StartNew();
            await using (var db = await factory.CreateDbContextAsync())
            {
                var pending = (await db.Database.GetPendingMigrationsAsync()).Count();
                db.Database.Migrate();
                Step($"1. OK Migrate(): {pending} migrations applied in {sw.ElapsedMilliseconds} ms");
            }
        }
        catch (Exception e) { Fail("1. Migrate", e); return; }

        // 2. Splits, a workout, streak and points
        try
        {
            var sw = Stopwatch.StartNew();
            var points = sp.GetRequiredService<PointsService>();
            await points.BackfillAllAsync();
            Step($"2. points backfill {sw.ElapsedMilliseconds} ms");

            var userId = await EnsureUserAsync(factory);
            var splits = sp.GetRequiredService<SplitService>();
            var presets = await splits.GetPresetsAsync();
            Step($"2. {presets.Count} presets: " + string.Join(", ", presets.Select(p => $"{p.Name} ({p.Days.Count} days, {p.Variants.Count} layouts)")));

            var split = presets[0];
            var today = AppClock.Today;
            if (!await splits.SelectAsync(userId, split.Id, split.Variants[0].Id, today.DayOfWeek))
                throw new Exception("SelectAsync returned false");

            var workouts = sp.GetRequiredService<WorkoutService>();
            var info = await workouts.GetCheckInAsync(userId);
            var dayId = info.PlannedDay?.SplitDayId ?? split.Days.OrderBy(d => d.Order).First().Id;
            await using (var db = await factory.CreateDbContextAsync())
            {
                var exerciseIds = await db.Exercises.Where(e => e.OwnerUserId == null).OrderBy(e => e.Id).Select(e => e.Id).Take(2).ToListAsync();
                foreach (var id in exerciseIds) await sp.GetRequiredService<DayExerciseService>().AddAsync(userId, dayId, id);
            }
            info = await workouts.GetCheckInAsync(userId, dayId);
            foreach (var ex in info.Exercises)
            {
                var saved = await sp.GetRequiredService<SetService>().SaveDayAsync(userId, ex.ExerciseId,
                    [new SetInput(SetKind.Working, 8, 60), new SetInput(SetKind.Working, 8, 62.5), new SetInput(SetKind.Working, 6, 65)]);
                if (!saved.Success) throw new Exception("SaveDayAsync: " + saved.Error);
            }
            var log = await workouts.LogTodayAsync(userId, info.PlannedDay is null ? dayId : null);
            if (!log.Success) throw new Exception("LogTodayAsync: " + log.Error);

            var streak = await sp.GetRequiredService<StreakService>().RecalculateAsync(userId);
            var overview = await points.GetAsync(userId);
            Step($"2. OK logged {info.Day?.Name} with {info.Exercises.Count} exercises; streak {streak.Current} " +
                 $"(best {streak.Longest}, {streak.Status}); points {overview?.Total}, level {overview?.Level.Number} {overview?.Level.Name}, " +
                 $"{overview?.History.Count} ledger lines; {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception e) { Fail("2. Services", e); }

        // 3. The map is on screen; tiles load by themselves
        Step("3. map shown (pan and pinch by hand)");

        // 4. The server
        try
        {
            var url = ServerUrl().TrimEnd('/') + "/health";
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var sw = Stopwatch.StartNew();
            using var resp = await http.GetAsync(url);
            var body = await resp.Content.ReadAsStringAsync();
            Step($"4. {(resp.IsSuccessStatusCode ? "OK" : "FAIL")} GET {url}: {(int)resp.StatusCode} \"{body}\" in {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception e) { Fail("4. /health", e); }

        Step("Done");
    }

    /// <summary>The folder the real app would use (AppDbContext.DataDirectory), so the spike shows where it lands per platform.</summary>
    private static string DataDir()
    {
        var dir = AppDbContext.DataDirectory;
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Desktop's BuildSettings reads AssemblyMetadata; logs whether that survives the platform's trimmer.</summary>
    private static string ServerUrl()
    {
        var fromMetadata = typeof(SpikeRunner).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "GymAppServerUrl")?.Value;
        SpikeLog.Write($"4. AssemblyMetadata GymAppServerUrl: {(fromMetadata is null ? "MISSING (trimmed)" : "present")}");
        return fromMetadata ?? BuiltServerUrl.Value;
    }

    private static async Task<int> EnsureUserAsync(IDbContextFactory<AppDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == "spike");
        if (user is null)
        {
            user = new User { Username = "spike", Email = "spike@example.com", PasswordHash = PasswordHasher.Hash("spike-password") };
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }
        return user.Id;
    }
}
