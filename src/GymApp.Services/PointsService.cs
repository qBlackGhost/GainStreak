using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

/// <summary>What a sync changed, for the points notice. <see cref="Added"/> is in date order.</summary>
public record PointsSync(int OldTotal, int NewTotal, LevelInfo OldLevel, LevelInfo NewLevel, IReadOnlyList<PointsAward> Added)
{
    public bool LeveledUp => NewLevel.Number > OldLevel.Number;
}

/// <summary>One ledger line for the history screen.</summary>
public record PointsLine(DateOnly Date, PointsReason Reason, int Amount);

/// <summary>The user's total, level and ledger, newest first.</summary>
public record PointsOverview(int Total, LevelInfo Level, IReadOnlyList<PointsLine> History);

public class PointsService(IDbContextFactory<AppDbContext> dbFactory)
{
    /// <summary>Reads the cached total and the ledger. Doesn't sync; the hooks keep it current.</summary>
    public async Task<PointsOverview?> GetAsync(int userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var total = await db.Users.Where(u => u.Id == userId).Select(u => (int?)u.Points).FirstOrDefaultAsync();
        if (total is not { } points) return null;

        var history = await db.PointsEntries.AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.Date).ThenByDescending(p => p.Id)
            .Select(p => new PointsLine(p.Date, p.Reason, p.Amount))
            .ToListAsync();
        return new PointsOverview(points, LevelCalculator.For(points), history);
    }

    /// <summary>
    /// Brings the ledger in line with <see cref="PointsCalculator"/> for the current and the previous week.
    /// Entries dated before that window are frozen, so later rule or schedule changes can't rewrite them; the
    /// previous week stays open because a grace payment on Monday can still settle its bonus.
    /// Call it after "I went today", a calorie log, a schedule change, and at app start or sign-in.
    /// A user who was never backfilled gets a full <see cref="RebuildAsync"/> instead.
    /// </summary>
    public async Task<PointsSync?> SyncAsync(int userId, DateOnly? today = null)
    {
        var day = today ?? DateOnly.FromDateTime(DateTime.Now);
        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return null;

        if (!user.PointsBackfilled)
            return await ApplyAsync(db, user, await FirstLogAsync(db, userId, day), day, rebuild: true);
        return await ApplyAsync(db, user, WindowStart(day), day, rebuild: false);
    }

    /// <summary>The same as <see cref="SyncAsync"/>, with the window starting at the user's first log, so existing
    /// workouts and calorie logs earn their points. Sets <see cref="User.PointsBackfilled"/>.</summary>
    public async Task<PointsSync?> RebuildAsync(int userId, DateOnly? today = null)
    {
        var day = today ?? DateOnly.FromDateTime(DateTime.Now);
        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return null;
        return await ApplyAsync(db, user, await FirstLogAsync(db, userId, day), day, rebuild: true);
    }

    /// <summary>Runs the one-time rebuild for every user that hasn't had it. Call once at startup.</summary>
    public async Task BackfillAllAsync(DateOnly? today = null)
    {
        List<int> ids;
        await using (var db = await dbFactory.CreateDbContextAsync())
            ids = await db.Users.Where(u => !u.PointsBackfilled).Select(u => u.Id).ToListAsync();

        foreach (var id in ids)
            await RebuildAsync(id, today);
    }

    /// <summary>The Monday of the previous week: the first day the sync may still change.</summary>
    public static DateOnly WindowStart(DateOnly today) => PointsCalculator.MondayOf(today).AddDays(-7);

    private static async Task<DateOnly> FirstLogAsync(AppDbContext db, int userId, DateOnly today)
    {
        var firstWorkout = await db.WorkoutLogs.Where(w => w.UserId == userId).MinAsync(w => (DateOnly?)w.Date);
        var firstCalories = await db.CalorieLogs.Where(c => c.UserId == userId).MinAsync(c => (DateOnly?)c.Date);
        return new[] { firstWorkout, firstCalories, today }.Where(d => d is not null).Min()!.Value;
    }

    private static async Task<PointsSync> ApplyAsync(AppDbContext db, User user, DateOnly windowStart, DateOnly today, bool rebuild)
    {
        var userId = user.Id;

        // All workout dates and schedule rows: the streak replay needs them from the first workout on to know
        // each day's outcome (a debt can carry into the window). Calorie logs only matter inside the window.
        var workoutDates = await db.WorkoutLogs.Where(w => w.UserId == userId).Select(w => w.Date).ToListAsync();
        var schedule = await db.UserSchedules.Where(s => s.UserId == userId)
            .OrderBy(s => s.EffectiveFrom)
            .Select(s => new ScheduleChange(s.EffectiveFrom, s.WeekPattern))
            .ToListAsync();
        var calories = await db.CalorieLogs.Where(c => c.UserId == userId && c.Date >= windowStart)
            .Select(c => new CalorieRecord(c.Date, c.CaloriesConsumed, c.GoalAtTime))
            .ToListAsync();

        var timeline = StreakCalculator.Timeline(workoutDates, schedule, today,
            windowStart, PointsCalculator.MondayOf(today).AddDays(6));
        DateOnly? scheduleStart = schedule.Count == 0 ? null : schedule[0].EffectiveFrom;
        var wanted = PointsCalculator.Calculate(workoutDates, timeline, scheduleStart, calories, today, windowStart)
            .ToDictionary(a => a.DedupKey);

        var oldTotal = user.Points;
        var added = new List<PointsAward>();

        await using var tx = await db.Database.BeginTransactionAsync();

        var stored = await db.PointsEntries.Where(p => p.UserId == userId).ToListAsync();
        var storedKeys = stored.Select(p => p.DedupKey).ToHashSet();

        foreach (var entry in stored.Where(p => p.Date >= windowStart))
        {
            if (!wanted.TryGetValue(entry.DedupKey, out var w)) { db.PointsEntries.Remove(entry); continue; }
            // A +50 week can't turn into +25 by the rules, but keep the entry exact if it ever does
            entry.Reason = w.Reason;
            entry.Amount = w.Amount;
            entry.Date = w.Date;
        }

        foreach (var w in wanted.Values.Where(w => !storedKeys.Contains(w.DedupKey)).OrderBy(w => w.Date))
        {
            db.PointsEntries.Add(new PointsEntry
                { UserId = userId, Reason = w.Reason, Amount = w.Amount, Date = w.Date, DedupKey = w.DedupKey });
            added.Add(w);
        }

        await db.SaveChangesAsync();

        user.Points = await db.PointsEntries.Where(p => p.UserId == userId).SumAsync(p => p.Amount);
        if (rebuild) user.PointsBackfilled = true;
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        return new PointsSync(oldTotal, user.Points, LevelCalculator.For(oldTotal), LevelCalculator.For(user.Points), added);
    }
}
