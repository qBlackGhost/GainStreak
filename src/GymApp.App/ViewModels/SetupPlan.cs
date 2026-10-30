using System.Collections.Generic;
using System.Linq;
using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Services;

namespace GymApp.App.ViewModels;

/// <summary>What one step of the "Get set up" card says; <see cref="Result"/> is null while it isn't done.</summary>
public record SetupStepText(string Title, string Text, string Cta, string? Result);

/// <summary>The wording of the dashboard's "Get set up" steps 2 and 3, from what the user already has.
/// Pure, so the rules are easy to test; <see cref="HomeViewModel"/> turns them into cards.</summary>
public static class SetupPlan
{
    /// <summary>Step 2 is done once every workout day of the split has at least one exercise. Its button always
    /// opens Workouts, where every day is listed.</summary>
    public static SetupStepText Exercises(WorkoutMenu? menu)
    {
        const string title = "Add exercises to every workout day", cta = "Open Workouts";
        if (menu is null || menu.Days.Count == 0)
            return new(title, "Pick a few exercises and planned sets for each day of your split, so every workout is ready to check in.",
                cta, null);

        var days = menu.Days;
        var filled = days.Count(d => d.ExerciseCount > 0);
        if (filled == days.Count)
            return new(title, "Every day of your split has exercises. Change them any time in Workouts.", cta,
                $"All {days.Count} day{(days.Count == 1 ? "" : "s")} have exercises");

        var text = filled == 0
            ? $"Your split has {days.Count} workout day{(days.Count == 1 ? "" : "s")}: {JoinList(days.Select(d => d.Name).ToList())}. " +
              "Give each one a few exercises and planned sets."
            : $"{filled} of {days.Count} days are ready. {JoinList(days.Where(d => d.ExerciseCount == 0).Select(d => d.Name).ToList())} " +
              $"still need{(days.Count - filled == 1 ? "s" : "")} exercises.";
        return new(title, text, cta, null);
    }

    /// <summary>The body data the maintenance estimate needs that is still missing. Gender is left out: "Prefer not
    /// to say" is saved as no gender, so it can't be told apart from a skipped answer.</summary>
    public static List<string> MissingBodyData(UserProfile profile, WeightLog? currentWeight)
    {
        var missing = new List<string>();
        if (profile.BirthDate is null) missing.Add("birth date");
        if (profile.HeightCm is null) missing.Add("height");
        if (currentWeight is null) missing.Add("weight");
        if (profile.ActivityLevel is null) missing.Add("activity");
        return missing;
    }

    /// <summary>Step 3: body data first (in Profile), then the calorie goal (in Calories). A goal already set
    /// finishes it either way. <c>OpensProfile</c> says which screen the button opens.</summary>
    public static (SetupStepText Step, bool OpensProfile) Calories(CalorieGoalInfo? goal, ProfileSummary? profile)
    {
        if (goal is { Goal: { } kcal } g)
            return (new("Set your calorie goal", "", "Open Calories", $"{CalorieGoals.DisplayName(g.GoalType)} · {CalorieGoals.K(kcal)} kcal"), false);

        var missing = profile is null ? ["birth date", "height", "weight", "activity"] : MissingBodyData(profile.Profile, profile.CurrentWeight);
        if (missing.Count > 0)
            return (new("Fill in your body data",
                $"Add your {JoinList(missing)} in Profile. They give you a maintenance estimate to base your calorie goal on.",
                "Open Profile", null), true);

        var text = profile?.Maintenance.Kcal is { } m
            ? $"Your maintenance is about {CalorieGoals.K(m)} kcal a day. Choose lose, maintain or gain to get a suggested daily target."
            : "Choose lose, maintain or gain, or enter your own daily target.";
        return (new("Set your calorie goal", text, "Open Calories", null), false);
    }

    private static string JoinList(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        2 => $"{items[0]} and {items[1]}",
        _ => $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}"
    };
}
