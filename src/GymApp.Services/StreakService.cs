using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

public class StreakService(IDbContextFactory<AppDbContext> dbFactory)
{
    /// <summary>
    /// Recomputes the streak from the logs and caches it on the user. Call it after every workout log
    /// change, after a schedule change, and when the app starts or a user signs in, because a streak can
    /// break at midnight without anything being logged.
    /// The best streak never shrinks, except with <paramref name="undoneToday"/> (today's check-in was just
    /// undone): if today's streak was the best, the best goes back with it. The returned Longest is then the stored one.
    /// </summary>
    public async Task<StreakResult> RecalculateAsync(int userId, DateOnly? today = null, bool undoneToday = false)
    {
        var day = today ?? AppClock.Today;
        await using var db = await dbFactory.CreateDbContextAsync();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return new StreakResult(0, 0, StreakStatus.NotStarted);

        var dates = await db.WorkoutLogs.Where(w => w.UserId == userId).Select(w => w.Date).ToListAsync();
        var schedule = await db.UserSchedules.Where(s => s.UserId == userId)
            .OrderBy(s => s.EffectiveFrom)
            .Select(s => new ScheduleChange(s.EffectiveFrom, s.WeekPattern))
            .ToListAsync();

        // Users who picked a split before schedule history existed: create their first entry from the
        // current selection. The first entry also covers earlier days, so nothing else is needed.
        if (schedule.Count == 0 && user.SelectedSplitId is { } splitId)
        {
            var pattern = await PatternFromSelectionAsync(db, user, splitId);
            if (pattern is not null)
            {
                db.UserSchedules.Add(new UserSchedule
                    { UserId = userId, EffectiveFrom = day, WeekPattern = pattern, SplitId = splitId });
                schedule.Add(new ScheduleChange(day, pattern));
            }
        }

        var choices = await LoadChoicesAsync(db, userId);
        var result = StreakCalculator.Calculate(dates, schedule, choices, day);

        // The cached values still describe the streak with today's log in it
        var todayWasBest = undoneToday && user.LongestStreak <= user.CurrentStreak;

        user.CurrentStreak = result.Current;
        user.LongestStreak = todayWasBest
            ? Math.Max(result.Longest, result.Current)                // back to what the logs give without today
            : Math.Max(user.LongestStreak, result.Longest);           // a personal best never shrinks
        await db.SaveChangesAsync();
        return undoneToday ? result with { Longest = user.LongestStreak } : result;
    }

    /// <summary>The streak as of <paramref name="today"/>, without saving anything. With <paramref name="beforeTodaysLog"/>
    /// today's log is left out, which gives the state the user was in when they checked in.</summary>
    public async Task<StreakResult> GetAsync(int userId, DateOnly today, bool beforeTodaysLog = false)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var (dates, schedule, choices) = await LoadAsync(db, userId);
        if (beforeTodaysLog) dates.Remove(today);
        return StreakCalculator.Calculate(dates, schedule, choices, today);
    }

    /// <summary>
    /// The day whose workout is done today, when it isn't today: while a "make it up before my next rest day" chain
    /// (or an old miss with no choice) is open, today's workout is yesterday's. That holds before and after today's
    /// check-in, and on the rest day that ends the chain. Null otherwise, including after "Shift my whole schedule"
    /// (the schedule itself moved) and while the choice is still to be made.
    /// </summary>
    public async Task<DateOnly?> MakeUpDayAsync(int userId, DateOnly today)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var (dates, schedule, choices) = await LoadAsync(db, userId);
        dates.Remove(today);
        var before = StreakCalculator.Calculate(dates, schedule, choices, today);
        var yesterday = today.AddDays(-1);
        return before is { Status: StreakStatus.AtRisk, OwedDay: { } owed } && owed == yesterday
               && (!choices.TryGetValue(owed, out var c) || c != MissChoice.ShiftWeek)
            ? owed
            : null;
    }

    /// <summary>The dashboard's "This week" row: Monday to Sunday of the week containing <paramref name="today"/>.</summary>
    public async Task<IReadOnlyList<WeekChip>> GetWeekAsync(int userId, DateOnly today)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var (dates, schedule, choices) = await LoadAsync(db, userId);
        var monday = PointsCalculator.MondayOf(today);
        return WeekRow.Build(StreakCalculator.Timeline(dates, schedule, choices, today, monday, monday.AddDays(6)), today);
    }

    /// <summary>The missed day the streak last broke on, or null if it never broke.</summary>
    public async Task<DateOnly?> LastBreakAsync(int userId, DateOnly today)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var (dates, schedule, choices) = await LoadAsync(db, userId);
        return StreakCalculator.LastBreak(dates, schedule, choices, today);
    }

    private static async Task<(List<DateOnly> Dates, List<ScheduleChange> Schedule, Dictionary<DateOnly, MissChoice> Choices)>
        LoadAsync(AppDbContext db, int userId)
    {
        var dates = await db.WorkoutLogs.Where(w => w.UserId == userId).Select(w => w.Date).ToListAsync();
        var schedule = await db.UserSchedules.Where(s => s.UserId == userId)
            .OrderBy(s => s.EffectiveFrom)
            .Select(s => new ScheduleChange(s.EffectiveFrom, s.WeekPattern))
            .ToListAsync();
        return (dates, schedule, await LoadChoicesAsync(db, userId));
    }

    /// <summary>The user's missed-day choices, keyed by the missed date, as the calculator takes them.</summary>
    internal static async Task<Dictionary<DateOnly, MissChoice>> LoadChoicesAsync(AppDbContext db, int userId) =>
        await db.MissedDayChoices.Where(c => c.UserId == userId).ToDictionaryAsync(c => c.Date, c => c.Choice);

    private static async Task<string?> PatternFromSelectionAsync(AppDbContext db, User user, int splitId)
    {
        var split = await db.WorkoutSplits.AsNoTracking()
            .Include(s => s.Days).Include(s => s.Variants)
            .FirstOrDefaultAsync(s => s.Id == splitId);
        var resolved = split is null ? null : SplitSchedule.Resolve(split, user);
        return resolved is null ? null : WeekPattern.From(resolved.Week);
    }
}
