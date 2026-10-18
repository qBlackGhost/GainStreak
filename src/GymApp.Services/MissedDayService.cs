using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

/// <summary>One option on the missed-day card. <see cref="NewWeek"/> is the 7 days from today after a
/// "Shift my whole schedule", for the small week row; null for the other options.</summary>
public record MissedDayOption(MissChoice Choice, string Title, string Preview, string ConfirmText,
    IReadOnlyList<WeekPreviewDay>? NewWeek = null);

/// <summary>What Home shows the day after a missed training day. <see cref="Options"/> are the ones available;
/// <see cref="Notes"/> say why a used-up option is hidden.</summary>
public record MissedDayPrompt(DateOnly MissedDate, string MissedDayName, int StreakAtStake, int ShiftWeeksLeft,
    int MakeUpsLeft, IReadOnlyList<MissedDayOption> Options, IReadOnlyList<string> Notes);

public record MissedDayResult(bool Success, string? Error = null, StreakResult? Streak = null, PointsSync? Points = null);

/// <summary>
/// The missed-day choice: the day after a missed training day with a streak at stake, the user picks
/// "Shift my whole schedule by a day", "Make it up before my next rest day" or "Skip it and continue".
/// The choice is stored (one <see cref="MissedDayChoice"/> per missed day) and final.
/// </summary>
public class MissedDayService(IDbContextFactory<AppDbContext> dbFactory, StreakService streaks, PointsService points)
{
    public const string ShiftWeekTitle = "Shift my whole schedule by a day";
    public const string MakeUpTitle = "Make it up before my next rest day";
    public const string ContinueTitle = "Skip it and continue";

    /// <summary>The card's contents, or null unless the streak status is <see cref="StreakStatus.NeedsChoice"/>.</summary>
    public async Task<MissedDayPrompt?> GetPromptAsync(int userId, DateOnly? today = null)
    {
        var day = today ?? DateOnly.FromDateTime(DateTime.Now);
        var streak = await streaks.GetAsync(userId, day);
        if (streak is not { Status: StreakStatus.NeedsChoice, OwedDay: { } missed }) return null;

        await using var db = await dbFactory.CreateDbContextAsync();
        return await BuildPromptAsync(db, userId, streak.Current, missed, day);
    }

    /// <summary>
    /// Stores the choice for yesterday's missed day, then recalculates the streak and syncs points. Refused (with a
    /// message, never an exception) unless the status is still NeedsChoice, the missed day is yesterday and the option
    /// is available. "Shift my whole schedule" also moves the split one day later and records the new week from today.
    /// </summary>
    public async Task<MissedDayResult> ChooseAsync(int userId, MissChoice choice, DateOnly? today = null)
    {
        var day = today ?? DateOnly.FromDateTime(DateTime.Now);
        var streak = await streaks.GetAsync(userId, day);
        if (streak is not { Status: StreakStatus.NeedsChoice, OwedDay: { } missed } || missed != day.AddDays(-1))
            return new MissedDayResult(false, "There's no missed workout to choose for today.");

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var prompt = await BuildPromptAsync(db, userId, streak.Current, missed, day);
            if (prompt.Options.All(o => o.Choice != choice))
                return new MissedDayResult(false, prompt.Notes.FirstOrDefault() ?? "That option isn't available.");

            await using var tx = await db.Database.BeginTransactionAsync();
            db.MissedDayChoices.Add(new MissedDayChoice { UserId = userId, Date = missed, Choice = choice });
            if (choice == MissChoice.ShiftWeek && !await ShiftWeekAsync(db, userId, day))
                return new MissedDayResult(false, "Pick a split first.");
            try
            {
                await db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch (DbUpdateException)
            {
                // the unique (user, date) index: another tap or window already chose
                return new MissedDayResult(false, "You've already chosen what to do about that day.");
            }
        }

        var result = await streaks.RecalculateAsync(userId, day);
        return new MissedDayResult(true, Streak: result, Points: await points.SyncAsync(userId, day));
    }

    private async Task<MissedDayPrompt> BuildPromptAsync(AppDbContext db, int userId, int atStake, DateOnly missed, DateOnly today)
    {
        var choices = await StreakService.LoadChoicesAsync(db, userId);
        var (week, names, hasSplit) = await CurrentWeekAsync(db, userId, today);
        var missedName = TodayPlan.Resolve(week, names, missed).Title;

        var shiftsLeft = MissedDayRules.Left(MissChoice.ShiftWeek, missed, choices);
        var makeUpsLeft = MissedDayRules.Left(MissChoice.ShiftToRest, missed, choices);
        var options = new List<MissedDayOption>();
        var notes = new List<string>();

        if (hasSplit)
        {
            if (shiftsLeft > 0)
                options.Add(new MissedDayOption(MissChoice.ShiftWeek, ShiftWeekTitle,
                    MissedDayRules.ShiftWeekText(week, names, missed),
                    $"Shift your whole schedule by a day? {missedName} today, and every day after moves one day later. This can't be changed later.",
                    MissedDayRules.Preview(MissedDayRules.ShiftedWeek(week), names, today)));
            else notes.Add(MissedDayRules.UsedUpText(MissChoice.ShiftWeek));
        }

        if (MissedDayRules.HasRestDay(week))
        {
            if (makeUpsLeft > 0)
                options.Add(new MissedDayOption(MissChoice.ShiftToRest, MakeUpTitle,
                    MissedDayRules.MakeUpText(week, names, today),
                    $"Make up {missedName} before your next rest day? This can't be changed later."));
            else notes.Add(MissedDayRules.UsedUpText(MissChoice.ShiftToRest));
        }

        options.Add(new MissedDayOption(MissChoice.Continue, ContinueTitle,
            MissedDayRules.ContinueText(week, names, today),
            $"Reset your {atStake}-day streak to 0?"));

        return new MissedDayPrompt(missed, missedName, atStake, shiftsLeft, makeUpsLeft, options, notes);
    }

    /// <summary>The user's current week and day names. Without a usable split it's the latest schedule row (or every
    /// day a training day with no schedule at all), with every training day called "Workout".</summary>
    private static async Task<(IReadOnlyList<ScheduleSlot> Week, IReadOnlyList<string> Names, bool HasSplit)> CurrentWeekAsync(
        AppDbContext db, int userId, DateOnly today)
    {
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
        if (user.SelectedSplitId is { } splitId
            && await LoadSplitAsync(db, splitId, tracked: false) is { } split
            && SplitSchedule.Resolve(split, user) is { } resolved)
            return (resolved.Week, resolved.Days.Select(d => d.Name).ToList(), true);

        var pattern = await db.UserSchedules.AsNoTracking()
            .Where(s => s.UserId == userId && s.EffectiveFrom <= today)
            .OrderByDescending(s => s.EffectiveFrom)
            .Select(s => s.WeekPattern)
            .FirstOrDefaultAsync() ?? "WWWWWWW";
        return (WeekSchedule.Build(pattern, DayOfWeek.Monday, 1), ["Workout"], false);
    }

    /// <summary>Moves the selected split one day later: a preset's start day, or every weekday of a custom split.
    /// Then records the new week from today (replacing a row already there for today). Changes are saved by the caller.</summary>
    private static async Task<bool> ShiftWeekAsync(AppDbContext db, int userId, DateOnly today)
    {
        var user = await db.Users.FirstAsync(u => u.Id == userId);
        if (user.SelectedSplitId is not { } splitId || await LoadSplitAsync(db, splitId, tracked: true) is not { } split)
            return false;

        if (split.OwnerUserId is null)
            user.SplitStartDay = Next(user.SplitStartDay ?? DayOfWeek.Monday);
        else
        {
            foreach (var d in split.Days.Where(d => d.Weekday is not null))
                d.Weekday = Next(d.Weekday!.Value);
            // Custom splits rotate Monday-first by Order, so the day that wrapped from Sunday to Monday comes first now
            var order = 1;
            foreach (var d in split.Days.OrderBy(d => d.Weekday is null).ThenBy(d => ((int)(d.Weekday ?? 0) + 6) % 7))
                d.Order = order++;
        }

        if (SplitSchedule.Resolve(split, user) is not { } resolved) return false;
        var pattern = WeekPattern.From(resolved.Week);
        var row = await db.UserSchedules.FirstOrDefaultAsync(s => s.UserId == userId && s.EffectiveFrom == today);
        if (row is null)
            db.UserSchedules.Add(new UserSchedule { UserId = userId, EffectiveFrom = today, WeekPattern = pattern, SplitId = splitId });
        else
        {
            row.WeekPattern = pattern;
            row.SplitId = splitId;
        }
        return true;
    }

    private static Task<WorkoutSplit?> LoadSplitAsync(AppDbContext db, int splitId, bool tracked)
    {
        var q = db.WorkoutSplits.Include(s => s.Days).Include(s => s.Variants);
        return (tracked ? q : q.AsNoTracking()).FirstOrDefaultAsync(s => s.Id == splitId);
    }

    private static DayOfWeek Next(DayOfWeek d) => (DayOfWeek)(((int)d + 1) % 7);
}
