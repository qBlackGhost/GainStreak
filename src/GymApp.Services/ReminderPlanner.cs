using System;
using System.Collections.Generic;
using System.Linq;
using GymApp.Core;

namespace GymApp.Services;

public enum ReminderKind
{
    /// <summary>A training day with no check-in yet, at the user's time.</summary>
    TrainingDay,
    /// <summary>The morning after a missed training day: the missed-day choice is waiting.</summary>
    MissedDay,
    /// <summary>The first morning after a vacation (0.19.0): the streak counts again.</summary>
    WelcomeBack
}

/// <summary>One local notification: at <see cref="Date"/> <see cref="Time"/> on the phone's clock (so 18:00 stays 18:00
/// across a daylight saving change). <see cref="Id"/> is the same for the same kind and day, every time it's planned.</summary>
public record PlannedReminder(ReminderKind Kind, DateOnly Date, TimeOnly Time, string Title, string Body)
{
    public int Id => Kind switch { ReminderKind.TrainingDay => 1, ReminderKind.MissedDay => 2, _ => 3 } * 1_000_000
                     + Date.DayNumber % 1_000_000;

    public DateTime LocalTime => Date.ToDateTime(Time);

    /// <summary>The instant it fires in <paramref name="zone"/> (the phone's): 18:00 is 18:00 there on either side of a
    /// daylight saving change. A time the change skips fires an hour later.</summary>
    public DateTimeOffset At(TimeZoneInfo zone)
    {
        var local = LocalTime;
        if (zone.IsInvalidTime(local)) local = local.AddHours(1);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}

/// <summary>The user's reminder settings (Profile → Reminders, 16g).</summary>
public record ReminderSettings(bool Enabled, bool TrainingDay, TimeOnly TrainingTime, bool MissedDay)
{
    public static readonly TimeOnly DefaultTrainingTime = new(18, 0);
    public static readonly TimeOnly MissedDayTime = new(9, 0);
    public static readonly ReminderSettings Off = new(false, true, DefaultTrainingTime, true);
}

/// <summary>
/// Which reminders to schedule for the next <see cref="Days"/> days (16g). Pure: the heads schedule exactly this list,
/// replacing whatever they scheduled before, every time the app starts, comes back to the front or something changes
/// the streak (a check-in, an undo, a missed-day choice, a new schedule).
/// <list type="bullet">
/// <item><b>Training day</b>, at the user's time: today when it's a training day (or a workout is owed) and nothing is
/// logged yet and the time hasn't passed; each later training day of the week, and tomorrow when a workout is owed
/// then (a shifted schedule).</item>
/// <item><b>Missed day</b>, at 09:00: this morning, while yesterday's miss waits for its choice; and the morning after
/// each training day planned above, while a streak would be at stake. The phone can't know ahead of time that a day
/// will be missed, so it plans for it: the check-in replans and drops it.</item>
/// <item><b>Vacation</b> (0.19.0): nothing on vacation days (<see cref="RestDayRule.Frozen"/>); "Welcome back" at 09:00
/// on the first normal day after one.</item>
/// </list>
/// The streak in a text is the one the user will have that day if they keep to the plan until then.
/// </summary>
public static class ReminderPlanner
{
    public const int Days = 7;

    /// <param name="workoutDates">Days with a logged workout.</param>
    /// <param name="dayName">The planned workout's name on a day ("Push"), or null if there's none to name.</param>
    public static IReadOnlyList<PlannedReminder> Plan(
        ReminderSettings settings,
        IEnumerable<DateOnly> workoutDates,
        IReadOnlyList<ScheduleChange> schedule,
        IReadOnlyDictionary<DateOnly, MissChoice> choices,
        DateTime now,
        Func<DateOnly, string?> dayName,
        RestDayRule? restRule = null)
    {
        if (!settings.Enabled || (!settings.TrainingDay && !settings.MissedDay)) return [];

        var today = DateOnly.FromDateTime(now);
        var clock = TimeOnly.FromDateTime(now);
        var dates = workoutDates.ToHashSet();
        var streak = StreakCalculator.Calculate(dates, schedule, choices, today, restRule);
        var loggedToday = dates.Contains(today);
        var result = new List<PlannedReminder>();

        // This morning: yesterday was missed and the choice is still to be made
        if (settings.MissedDay && streak.Status == StreakStatus.NeedsChoice && clock < ReminderSettings.MissedDayTime
            && streak.OwedDay is { } owed)
            result.Add(Missed(today, owed, streak.Current, dayName));

        // The days a workout is expected, and the streak going into each if the plan is kept
        var count = streak.Current;
        var todayCounted = streak.Status is StreakStatus.Active or StreakStatus.Shifted;   // already in Current
        for (var i = 0; i < Days; i++)
        {
            var day = today.AddDays(i);
            if (restRule?.IsFrozen(day) == true) continue;                  // on vacation: kept, nothing planned or added
            if (restRule?.IsFrozen(day.AddDays(-1)) == true && (i > 0 || clock < ReminderSettings.MissedDayTime))
                result.Add(WelcomeBack(day));
            var owedToday = i == 0 && !loggedToday && streak.Status is StreakStatus.AtRisk or StreakStatus.NeedsChoice;
            var owedTomorrow = i == 1 && streak.Status == StreakStatus.Shifted;
            var training = owedToday || owedTomorrow
                           || (StreakCalculator.IsTrainingDay(schedule, day) && !(i == 0 && loggedToday));

            if (training)
            {
                var name = i == 0 && streak.Status is StreakStatus.AtRisk && streak.OwedDay is { } o ? dayName(o) : dayName(day);
                if (settings.TrainingDay && (i > 0 || clock < settings.TrainingTime))
                    result.Add(Training(day, settings.TrainingTime, name, count));
                if (settings.MissedDay && count > 0 && i < Days - 1 && restRule?.IsFrozen(day.AddDays(1)) != true)
                    result.Add(Missed(day.AddDays(1), day, count, _ => name));
            }

            // The day ends: a planned workout adds one; a rest day adds one while there's a streak, until the
            // rest-day rule (0.18.1) starts: then it keeps the streak without adding (a verified workout can't be planned)
            if (i == 0 && todayCounted) continue;
            if (training) count++;
            else if (count > 0 && (restRule is null || day < restRule.From)) count++;
        }

        return result.OrderBy(r => r.LocalTime).ToList();
    }

    private static PlannedReminder Training(DateOnly day, TimeOnly time, string? name, int streak) => new(
        ReminderKind.TrainingDay, day, time,
        name is null ? "Training day" : $"Today is {name}",
        streak > 0
            ? $"Check in when you've trained to keep your {StreakDays(streak)} streak."
            : "Check in when you've trained to start your streak.");

    private static PlannedReminder WelcomeBack(DateOnly day) => new(
        ReminderKind.WelcomeBack, day, ReminderSettings.MissedDayTime,
        "Welcome back!", "Your vacation is over, so your streak counts again from today.");

    private static PlannedReminder Missed(DateOnly on, DateOnly missed, int streak, Func<DateOnly, string?> dayName) => new(
        ReminderKind.MissedDay, on, ReminderSettings.MissedDayTime,
        "Your streak is at stake",
        $"You missed {dayName(missed) ?? missed.DayOfWeek.ToString()} yesterday. Choose what happens to your {StreakDays(streak)} streak before midnight.");

    private static string StreakDays(int n) => n == 1 ? "1-day" : $"{n}-day";
}
