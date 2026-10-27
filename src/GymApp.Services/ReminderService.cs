using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Data;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

/// <summary>The reminder settings (Profile → Reminders) and the reminders they give right now (16g): what the
/// phones hand to their notification scheduler.</summary>
public class ReminderService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<ReminderSettings> GetSettingsAsync(int userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId);
        return u is null ? ReminderSettings.Off
            : new ReminderSettings(u.RemindersEnabled, u.TrainingReminderEnabled, u.TrainingReminderTime, u.MissedDayReminderEnabled);
    }

    public async Task SaveSettingsAsync(int userId, ReminderSettings settings)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == userId);
        if (u is null) return;
        u.RemindersEnabled = settings.Enabled;
        u.TrainingReminderEnabled = settings.TrainingDay;
        u.TrainingReminderTime = settings.TrainingTime;
        u.MissedDayReminderEnabled = settings.MissedDay;
        await db.SaveChangesAsync();
    }

    /// <summary>The reminders for the next week as of <paramref name="now"/> (the phone's local time), named after the
    /// split's days.</summary>
    public async Task<IReadOnlyList<PlannedReminder>> PlanAsync(int userId, DateTime now)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId);
        if (user is null || !user.RemindersEnabled) return [];
        var settings = new ReminderSettings(user.RemindersEnabled, user.TrainingReminderEnabled, user.TrainingReminderTime,
            user.MissedDayReminderEnabled);

        var (dates, schedule, choices) = await StreakService.LoadAsync(db, userId);
        return ReminderPlanner.Plan(settings, dates, schedule, choices, now, await DayNamesAsync(db, user));
    }

    /// <summary>The planned workout's name on a day ("Push"), from the selected split; null on its rest days or with
    /// no split.</summary>
    private static async Task<Func<DateOnly, string?>> DayNamesAsync(AppDbContext db, Data.Models.User user)
    {
        if (user.SelectedSplitId is not { } splitId) return _ => null;
        var split = await db.WorkoutSplits.AsNoTracking()
            .Include(s => s.Days).Include(s => s.Variants)
            .FirstOrDefaultAsync(s => s.Id == splitId);
        var resolved = split is null ? null : SplitSchedule.Resolve(split, user);
        if (resolved is null) return _ => null;
        var names = resolved.Days.Select(d => d.Name).ToList();
        return day => TodayPlan.Resolve(resolved.Week, names, day) is { IsTraining: true } w ? w.Title : null;
    }
}
