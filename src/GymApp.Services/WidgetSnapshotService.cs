using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GymApp.Core;
using GymApp.Data;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

/// <summary>
/// What the iPhone's Home Screen widgets show (Phase 24), written by the app as JSON into the App Group the widget
/// extension reads. The extension never opens the database: everything it draws is in here, already formatted.
/// A wire format between C# and Swift (src/GymApp.iOS.LiveActivity/Widget/HomeWidgets.swift): add fields freely, but
/// rename or remove one only together with the Swift side.
/// </summary>
/// <param name="Date">The day the snapshot is for (<see cref="AppClock.Today"/>), "yyyy-MM-dd". The widget shows
/// <see cref="Tomorrow"/> after midnight and asks to open the app once even that has passed.</param>
/// <param name="SignedIn">False after sign-out: the widgets ask to sign in and show no one's data.</param>
public record WidgetSnapshot(
    int Version,
    string Date,
    bool SignedIn,
    WidgetStreak? Streak = null,
    WidgetLevel? Level = null,
    WidgetDay? Today = null,
    WidgetDay? Tomorrow = null,
    WidgetWeight? Weight = null,
    WidgetSteps? Steps = null)
{
    public const int CurrentVersion = 1;

    public static WidgetSnapshot SignedOut(DateOnly today) => new(CurrentVersion, WidgetFormat.Day(today), false);

    public string ToJson() => JsonSerializer.Serialize(this, WidgetFormat.Json);
}

/// <param name="Status">
/// <c>done</c> (today's workout is logged), <c>live</c> (a workout is open), <c>pending</c> (a training day, not logged
/// yet), <c>rest</c> (a rest day), <c>atRisk</c> (a missed day is owed), <c>needsChoice</c> (yesterday was missed and the
/// app is waiting for a choice), <c>broken</c>, <c>notStarted</c> (no split chosen yet) or <c>vacation</c>.
/// </param>
/// <param name="VacationUntil">On vacation: the return date, the first normal day again ("yyyy-MM-dd"), so the widget
/// knows whether tomorrow is still frozen.</param>
public record WidgetStreak(int Current, string Status, string? VacationUntil = null);

/// <param name="Progress">0 to 1 within the level.</param>
/// <param name="ToNext">Points to <paramref name="NextName"/>; both null at the top level.</param>
public record WidgetLevel(int Number, string Name, int Points, double Progress, string? NextName, int? ToNext);

/// <summary>A day of the active split. <see cref="Name"/> is null on a rest day; <see cref="HasSplit"/> is false
/// with no split chosen.</summary>
public record WidgetDay(string Date, bool HasSplit, string? Name, IReadOnlyList<WidgetExercise> Exercises, int ExerciseCount);

/// <param name="Last">The heaviest working set last time, as Home shows it ("100 kg × 5"), or "First time".</param>
public record WidgetExercise(string Name, string Last);

/// <param name="Current">The latest weigh-in in the user's unit ("78.4").</param>
/// <param name="Change7">Against the weigh-in a week before ("−0.6", "+1.2", "0"), null without one that old.</param>
/// <param name="Trend30">The last 30 days' weigh-ins in the user's unit, oldest first, for the chart.</param>
public record WidgetWeight(string Current, string Unit, string? Change7, string? Change30, IReadOnlyList<double> Trend30);

/// <param name="Week">The last 7 days' totals, today last (today so far).</param>
public record WidgetSteps(int Today, IReadOnlyList<int> Week);

public static class WidgetFormat
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>A weight change with its sign: "−0.6" (a real minus), "+1.2", "0".</summary>
    public static string Change(double change)
    {
        var rounded = Math.Round(change, 1, MidpointRounding.AwayFromZero);
        if (rounded == 0) return "0";
        var text = Math.Abs(rounded).ToString("0.#", CultureInfo.InvariantCulture);
        return rounded < 0 ? "−" + text : "+" + text;
    }
}

/// <summary>Builds the <see cref="WidgetSnapshot"/> with the same rules Home uses (Phase 24).</summary>
public class WidgetSnapshotService(IDbContextFactory<AppDbContext> dbFactory, WorkoutService workouts, StreakService streaks,
    LiveWorkoutService live)
{
    /// <summary>How many exercises a day carries: the large widget shows up to this many.</summary>
    public const int MaxExercises = 5;

    /// <param name="steps">The last 7 days' step totals from Apple Health, today last; null when Health is off or
    /// can't be read.</param>
    public async Task<WidgetSnapshot> BuildAsync(int userId, DateOnly today, IReadOnlyList<double>? steps = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
        var units = await db.UserProfiles.AsNoTracking().Where(p => p.UserId == userId).Select(p => (UnitSystem?)p.Units)
            .FirstOrDefaultAsync() ?? UnitSystem.Metric;

        var todayInfo = await workouts.GetTodayAsync(userId, today);
        var isLive = await live.IsOpenAsync(userId);
        // As on Home: during a live workout today's log may already exist (written at half), and doesn't count yet
        var streak = await streaks.GetAsync(userId, today, beforeTodaysLog: isLive);
        var status = Status(streak, todayInfo, isLive);

        var level = LevelCalculator.For(user.Points);
        return new WidgetSnapshot(
            WidgetSnapshot.CurrentVersion,
            WidgetFormat.Day(today),
            true,
            new WidgetStreak(streak.Current, status, streak.VacationUntil is { } until ? WidgetFormat.Day(until) : null),
            new WidgetLevel(level.Number, level.Name, user.Points, level.Progress, level.NextName, level.PointsToNext(user.Points)),
            await DayAsync(userId, today),
            await DayAsync(userId, today.AddDays(1)),
            await WeightAsync(db, userId, today, units),
            Steps(steps));
    }

    private static string Status(StreakResult streak, TodayInfo today, bool isLive)
    {
        if (streak.VacationUntil is not null) return "vacation";
        if (isLive) return "live";
        if (today.Logged) return "done";
        if (!today.HasSplit) return "notStarted";
        return streak.Status switch
        {
            StreakStatus.AtRisk => "atRisk",
            StreakStatus.NeedsChoice => "needsChoice",
            StreakStatus.Broken => "broken",
            // A first workout still to do reads like any other training day
            _ => today.IsTraining ? "pending" : "rest",
        };
    }

    private async Task<WidgetDay> DayAsync(int userId, DateOnly date)
    {
        var day = await workouts.GetTodayWorkoutAsync(userId, date);
        var exercises = day.Exercises.Take(MaxExercises).Select(e => new WidgetExercise(e.Name,
            e.LastTopSet is { } s ? $"{NumberParsing.FormatKg(s.WeightKg)} kg × {s.Reps}" : "First time")).ToList();
        return new WidgetDay(WidgetFormat.Day(date), day.HasSplit, day.DayName, exercises, day.Exercises.Count);
    }

    private static async Task<WidgetWeight?> WeightAsync(AppDbContext db, int userId, DateOnly today, UnitSystem units)
    {
        var logs = await db.WeightLogs.AsNoTracking()
            .Where(w => w.UserId == userId && w.Date <= today)
            .OrderByDescending(w => w.Date).Select(w => new { w.Date, w.WeightKg }).Take(400).ToListAsync();
        if (logs.Count == 0) return null;

        double Show(double kg) => Math.Round(units == UnitSystem.Metric ? kg : Units.KgToLb(kg), 1);
        var latest = logs[0];
        string? ChangeSince(int days) =>
            logs.FirstOrDefault(w => w.Date <= today.AddDays(-days)) is { } then
                ? WidgetFormat.Change(Show(latest.WeightKg) - Show(then.WeightKg))
                : null;

        var trend = logs.Where(w => w.Date > today.AddDays(-30)).OrderBy(w => w.Date).Select(w => Show(w.WeightKg)).ToList();
        return new WidgetWeight(
            Show(latest.WeightKg).ToString("0.0", CultureInfo.InvariantCulture),
            units == UnitSystem.Metric ? "kg" : "lb",
            ChangeSince(7), ChangeSince(30), trend);
    }

    private static WidgetSteps? Steps(IReadOnlyList<double>? days)
    {
        if (days is not { Count: > 0 }) return null;
        var week = days.TakeLast(7).Select(d => (int)Math.Round(Math.Max(d, 0))).ToList();
        return new WidgetSteps(week[^1], week);
    }
}
