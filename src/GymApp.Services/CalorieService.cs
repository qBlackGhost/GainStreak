using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

/// <summary>The user's goal and the current suggestion. Suggestion is null when there is no estimate;
/// <see cref="SuggestionReason"/> then says why. <see cref="SuggestionChanged"/> means the suggestion moved
/// since the user last answered it (their weight, activity, or goal type changed), so ask before overwriting.</summary>
public record CalorieGoalInfo(
    GoalType GoalType,
    int? Goal,
    CalorieSuggestion? Suggestion,
    string? SuggestionReason,
    bool SuggestionChanged,
    Sex? Sex);

/// <summary>One logged day. Hit is null when no goal was set at the time.</summary>
public record CalorieDay(int Id, DateOnly Date, int Consumed, int? Goal, GoalType? GoalType, bool? Hit);

public record CalorieOverview(CalorieGoalInfo Goal, CalorieDay? Today, IReadOnlyList<CalorieDay> Recent);

public record CalorieResult(bool Success, string? Error = null, PointsSync? Points = null);

public class CalorieService(IDbContextFactory<AppDbContext> dbFactory, PointsService points)
{
    public const int RecentDayCount = 7;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public async Task<CalorieOverview?> GetAsync(int userId, DateOnly? today = null)
    {
        var day = today ?? Today;
        await using var db = await dbFactory.CreateDbContextAsync();

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return null;

        var goal = await GoalInfoAsync(db, user, day);
        var recent = (await db.CalorieLogs.AsNoTracking()
                .Where(c => c.UserId == userId)
                .OrderByDescending(c => c.Date)
                .Take(RecentDayCount)
                .ToListAsync())
            .Select(ToDay)
            .ToList();

        return new CalorieOverview(goal, recent.FirstOrDefault(d => d.Date == day), recent);
    }

    /// <summary>Switches lose / maintain / gain. The stored goal stays until the user accepts the new suggestion.</summary>
    public async Task<CalorieResult> SetGoalTypeAsync(int userId, GoalType goalType)
    {
        if (!Enum.IsDefined(goalType)) return new(false, "Pick lose, maintain, or gain.");

        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return new(false, "User not found.");

        user.GoalType = goalType;
        await db.SaveChangesAsync();
        return new(true);
    }

    /// <summary>Stores the current suggestion as the goal. Recomputed here, never taken from the screen.</summary>
    public async Task<CalorieResult> AcceptSuggestionAsync(int userId, DateOnly? today = null)
    {
        var day = today ?? Today;
        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return new(false, "User not found.");

        var info = await GoalInfoAsync(db, user, day);
        if (info.Suggestion is not { } s) return new(false, info.SuggestionReason ?? "There is no suggestion yet.");

        user.DailyCalorieGoal = s.Kcal;
        user.LastSuggestedGoal = s.Kcal;
        await db.SaveChangesAsync();
        return new(true);
    }

    /// <summary>Stores a number the user typed, within the safety limits.</summary>
    public async Task<CalorieResult> SetCustomGoalAsync(int userId, int kcal, DateOnly? today = null)
    {
        var day = today ?? Today;
        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return new(false, "User not found.");

        var info = await GoalInfoAsync(db, user, day);
        if (CalorieGoals.CheckGoal(kcal, info.Sex, info.Suggestion?.Maintenance) is { } error) return new(false, error);

        user.DailyCalorieGoal = kcal;
        user.LastSuggestedGoal = info.Suggestion?.Kcal;   // they have seen today's suggestion and chose their own
        await db.SaveChangesAsync();
        return new(true);
    }

    /// <summary>"Keep my goal": stop asking until the suggestion changes again.</summary>
    public async Task<CalorieResult> KeepGoalAsync(int userId, DateOnly? today = null)
    {
        var day = today ?? Today;
        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return new(false, "User not found.");

        var info = await GoalInfoAsync(db, user, day);
        user.LastSuggestedGoal = info.Suggestion?.Kcal;
        await db.SaveChangesAsync();
        return new(true);
    }

    /// <summary>Logs what the user ate today. Today only, like workouts; logging again today replaces the entry.
    /// The goal and goal type are snapshotted, since both can change later.</summary>
    public async Task<CalorieResult> LogTodayAsync(int userId, int consumed, DateOnly? today = null)
    {
        var day = today ?? Today;
        if (CalorieGoals.CheckConsumed(consumed) is { } error) return new(false, error);

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            if (user is null) return new(false, "User not found.");

            var entry = await db.CalorieLogs.FirstOrDefaultAsync(c => c.UserId == userId && c.Date == day);
            if (entry is null)
            {
                entry = new CalorieLog { UserId = userId, Date = day };
                db.CalorieLogs.Add(entry);
            }
            entry.CaloriesConsumed = consumed;
            entry.GoalAtTime = user.DailyCalorieGoal;
            entry.GoalTypeAtTime = user.DailyCalorieGoal is null ? null : user.GoalType;

            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException) { return new(false, "Could not save your calories."); }
        }
        return new(true, Points: await points.SyncAsync(userId, day));
    }

    private static async Task<CalorieGoalInfo> GoalInfoAsync(AppDbContext db, User user, DateOnly today)
    {
        var profile = await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == user.Id)
                      ?? new UserProfile { UserId = user.Id };
        var weight = await db.WeightLogs.AsNoTracking()
            .Where(w => w.UserId == user.Id)
            .OrderByDescending(w => w.Date)
            .Select(w => (double?)w.WeightKg)
            .FirstOrDefaultAsync();

        var estimate = ProfileService.Estimate(profile, weight, today);
        var suggestion = estimate.Kcal is { } tdee
            ? CalorieGoals.Suggest(user.GoalType, tdee, profile.Sex!.Value)
            : null;

        var changed = user.DailyCalorieGoal is { } goal && suggestion is { } s
                      && s.Kcal != goal && s.Kcal != user.LastSuggestedGoal;

        return new CalorieGoalInfo(user.GoalType, user.DailyCalorieGoal, suggestion, estimate.Reason, changed, profile.Sex);
    }

    private static CalorieDay ToDay(CalorieLog c) =>
        new(c.Id, c.Date, c.CaloriesConsumed, c.GoalAtTime, c.GoalTypeAtTime,
            c.GoalAtTime is { } g ? CalorieGoals.HitTarget(g, c.CaloriesConsumed) : null);
}
