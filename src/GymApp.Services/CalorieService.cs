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
    Sex? Sex,
    int? ProteinGoalGrams = null,
    int? CarbsGoalGrams = null,
    int? FatGoalGrams = null,
    MacroMode? MacroMode = null,
    MacroInputs? MacroInputs = null);

/// <summary>What the recommended macros are worked out from (Phase 27): sex, the weight to plan with (the 14-day trend, or the
/// basis weight while the trend is within a kilo of it), height, activity and the safe minimum. Null on
/// <see cref="CalorieGoalInfo"/> when body data is missing; <see cref="CalorieGoalInfo.SuggestionReason"/> then says what.</summary>
public record MacroInputs(Sex Sex, double WeightKg, double? HeightCm, ActivityLevel? Activity, int? Maintenance,
    UnitSystem Units = UnitSystem.Metric)
{
    public int SafeMinimum => CalorieGoals.SafeMinimum(Sex, Maintenance);
    public MacroRecommendation For(GoalType goal) => MacroPlan.Recommend(goal, Sex, WeightKg, HeightCm, Activity, SafeMinimum);
}

/// <summary>The recommended targets moved because the trend weight did: the new goal and how far the weight moved.</summary>
public record TargetsUpdate(int OldKcal, int NewKcal, double WeightChangeKg);

/// <summary>One logged day. Hit is null when no goal was set at the time.</summary>
public record CalorieDay(int Id, DateOnly Date, int Consumed, int? Goal, GoalType? GoalType, bool? Hit);

public record CalorieOverview(CalorieGoalInfo Goal, CalorieDay? Today, IReadOnlyList<CalorieDay> Recent, TargetsUpdate? TargetsUpdate = null);

public record CalorieResult(bool Success, string? Error = null, PointsSync? Points = null);

public class CalorieService(IDbContextFactory<AppDbContext> dbFactory, PointsService points)
{
    public const int RecentDayCount = 7;

    private static DateOnly Today => AppClock.Today;

    public async Task<CalorieOverview?> GetAsync(int userId, DateOnly? today = null)
    {
        var day = today ?? Today;
        await using var db = await dbFactory.CreateDbContextAsync();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return null;

        var update = await RefreshRecommendedAsync(db, user, day);
        var goal = await GoalInfoAsync(db, user, day);
        var recent = (await db.CalorieLogs.AsNoTracking()
                .Where(c => c.UserId == userId)
                .OrderByDescending(c => c.Date)
                .Take(RecentDayCount)
                .ToListAsync())
            .Select(ToDay)
            .ToList();

        return new CalorieOverview(goal, recent.FirstOrDefault(d => d.Date == day), recent, update);
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

    /// <summary>Recommended mode (Phase 27): the goal type's recommended macros become the targets and their calories the goal.
    /// Worked out here from the current body data and trend weight, never taken from the screen. They then follow the trend
    /// weight (a kilo at a time) and body data changes by themselves.</summary>
    public async Task<CalorieResult> UseRecommendedAsync(int userId, GoalType goalType, DateOnly? today = null)
    {
        if (!Enum.IsDefined(goalType)) return new(false, "Pick lose, maintain, or gain.");
        var day = today ?? Today;
        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return new(false, "User not found.");

        var (inputs, reason, trend) = await MacroInputsAsync(db, user, day, useBasis: false);
        if (inputs is null || trend is null) return new(false, reason ?? "Add your body data to get a recommendation.");

        user.GoalType = goalType;
        user.MacroMode = MacroMode.Recommended;
        user.MacroBasisWeightKg = trend;
        Apply(user, inputs.For(goalType));
        await db.SaveChangesAsync();
        return new(true);
    }

    /// <summary>Custom mode (Phase 27): the user's own grams, all three required (0 to 1,000). The goal is what they add up to,
    /// within the safety limits; it is never typed.</summary>
    public async Task<CalorieResult> SetCustomMacrosAsync(int userId, GoalType goalType, int? protein, int? carbs, int? fat,
        DateOnly? today = null)
    {
        if (!Enum.IsDefined(goalType)) return new(false, "Pick lose, maintain, or gain.");
        var error = MacroPlanChecks.Grams("Protein", protein) ?? MacroPlanChecks.Grams("Carbs", carbs) ?? MacroPlanChecks.Grams("Fat", fat);
        if (error is not null) return new(false, error);

        var day = today ?? Today;
        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return new(false, "User not found.");

        var kcal = FoodRules.KcalFromMacros(protein!.Value, carbs!.Value, fat!.Value);
        var info = await GoalInfoAsync(db, user, day);
        if (CalorieGoals.CheckGoal(kcal, info.Sex, info.MacroInputs?.Maintenance ?? info.Suggestion?.Maintenance) is { } goalError)
            return new(false, $"Your macros add up to {CalorieGoals.K(kcal)} kcal. {goalError}");

        user.GoalType = goalType;
        user.MacroMode = MacroMode.Custom;
        user.MacroBasisWeightKg = null;
        user.DailyCalorieGoal = kcal;
        user.LastSuggestedGoal = info.Suggestion?.Kcal;
        user.ProteinGoalGrams = protein;
        user.CarbsGoalGrams = carbs;
        user.FatGoalGrams = fat;
        await db.SaveChangesAsync();
        return new(true);
    }

    private static void Apply(User user, MacroRecommendation r)
    {
        user.DailyCalorieGoal = r.Kcal;
        user.LastSuggestedGoal = r.Kcal;
        user.ProteinGoalGrams = r.Protein;
        user.CarbsGoalGrams = r.Carbs;
        user.FatGoalGrams = r.Fat;
    }

    /// <summary>Recommended mode keeps its targets up to date: after a body data change right away, and after weigh-ins only once
    /// the 14-day trend is a kilo from the weight they were worked out from. Returns the change when the weight moved them.</summary>
    private static async Task<TargetsUpdate?> RefreshRecommendedAsync(AppDbContext db, User user, DateOnly today)
    {
        if (user.MacroMode != MacroMode.Recommended) return null;
        var (inputs, _, trend) = await MacroInputsAsync(db, user, today, useBasis: true);
        if (inputs is null || trend is null) return null;   // body data went missing: keep what is stored

        var oldBasis = user.MacroBasisWeightKg;
        var weightMoved = inputs.WeightKg != oldBasis;
        var r = inputs.For(user.GoalType);
        var oldKcal = user.DailyCalorieGoal;
        if (!weightMoved && r.Kcal == oldKcal && r.Protein == user.ProteinGoalGrams && r.Carbs == user.CarbsGoalGrams
            && r.Fat == user.FatGoalGrams)
            return null;

        user.MacroBasisWeightKg = inputs.WeightKg;
        Apply(user, r);
        await db.SaveChangesAsync();
        return weightMoved && oldBasis is { } b && oldKcal is { } k && k != r.Kcal
            ? new TargetsUpdate(k, r.Kcal, Math.Round(inputs.WeightKg - b, 1))
            : null;
    }

    /// <summary>The inputs for the recommendation, or why there are none. <paramref name="useBasis"/>: plan with the stored basis
    /// weight while the trend is within a kilo of it (recommended mode), so a day's weigh-in doesn't move the targets.</summary>
    private static async Task<(MacroInputs? Inputs, string? Reason, double? Trend)> MacroInputsAsync(AppDbContext db, User user,
        DateOnly today, bool useBasis)
    {
        var profile = await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == user.Id)
                      ?? new UserProfile { UserId = user.Id };
        var weighIns = await db.WeightLogs.AsNoTracking()
            .Where(w => w.UserId == user.Id && w.Date <= today)
            .OrderByDescending(w => w.Date)
            .Take(MacroPlan.TrendDays * 2)
            .Select(w => new { w.Date, w.WeightKg })
            .ToListAsync();
        var trend = MacroPlan.TrendWeight(weighIns.Select(w => (w.Date, w.WeightKg)), today);
        var estimate = ProfileService.Estimate(profile, weighIns.FirstOrDefault()?.WeightKg, today);
        if (estimate.Kcal is null || trend is not { } t) return (null, estimate.Reason, trend);

        var weight = useBasis && user.MacroBasisWeightKg is { } basis && !MacroPlan.TrendMoved(basis, t) ? basis : t;
        return (new MacroInputs(profile.Sex!.Value, weight, profile.HeightCm, profile.ActivityLevel, estimate.Kcal, profile.Units), null, t);
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

        var (inputs, _, _) = await MacroInputsAsync(db, user, today, useBasis: user.MacroMode == MacroMode.Recommended);
        return new CalorieGoalInfo(user.GoalType, user.DailyCalorieGoal, suggestion, estimate.Reason, changed, profile.Sex,
            user.ProteinGoalGrams, user.CarbsGoalGrams, user.FatGoalGrams, user.MacroMode, inputs);
    }

    /// <summary>One day's log, or null when nothing was logged that day (Phase 27's past-day popup).</summary>
    public async Task<CalorieDay?> GetDayAsync(int userId, DateOnly date)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var log = await db.CalorieLogs.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == userId && c.Date == date);
        return log is null ? null : ToDay(log);
    }

    private static CalorieDay ToDay(CalorieLog c) =>
        new(c.Id, c.Date, c.CaloriesConsumed, c.GoalAtTime, c.GoalTypeAtTime,
            c.GoalAtTime is { } g ? CalorieGoals.HitTarget(g, c.CaloriesConsumed) : null);
}
