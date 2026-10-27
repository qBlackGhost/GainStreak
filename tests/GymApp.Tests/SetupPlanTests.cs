using GymApp.Core;
using GymApp.Data.Models;
using GymApp.App.ViewModels;
using GymApp.Services;

namespace GymApp.Tests;

public class SetupPlanTests
{
    private static MenuDay Day(int id, string name, int exercises) => new(id, name, "", exercises, false, []);
    private static WorkoutMenu Menu(params MenuDay[] days) => new("Push / Pull / Legs", false, days, []);

    // --- Step 2: exercises for every day ---

    [Fact] public void Exercises_WithoutASplit_OpensWorkouts()
    {
        var (step, open) = SetupPlan.Exercises(null, null);

        Assert.Equal(("Add exercises to every workout day", "Open Workouts"), (step.Title, step.Cta));
        Assert.Null(step.Result);
        Assert.Null(open);
    }

    [Fact] public void Exercises_NothingYet_NamesEveryDay_AndOpensTodayFirst()
    {
        var (step, open) = SetupPlan.Exercises(Menu(Day(1, "Push", 0), Day(2, "Pull", 0), Day(3, "Legs", 0)), new CheckInDay(2, "Pull"));

        Assert.Equal("Your split has 3 workout days: Push, Pull and Legs. Give each one a few exercises and planned sets.", step.Text);
        Assert.Equal("Open Pull", step.Cta);
        Assert.Equal(2, open!.SplitDayId);
        Assert.Null(step.Result);
    }

    [Fact] public void Exercises_SomeDaysDone_IsNotDone_AndOpensTheFirstEmptyDay()
    {
        // Today's day (Push) is filled, which used to finish the step
        var (step, open) = SetupPlan.Exercises(Menu(Day(1, "Push", 4), Day(2, "Pull", 0), Day(3, "Legs", 0)), new CheckInDay(1, "Push"));

        Assert.Null(step.Result);
        Assert.Equal("1 of 3 days are ready. Pull and Legs still need exercises.", step.Text);
        Assert.Equal(("Open Pull", 2), (step.Cta, open!.SplitDayId));
    }

    [Fact] public void Exercises_OneDayLeft_SaysWhich()
    {
        var (step, _) = SetupPlan.Exercises(Menu(Day(1, "Upper", 5), Day(2, "Lower", 0)), null);

        Assert.Equal("1 of 2 days are ready. Lower still needs exercises.", step.Text);
    }

    [Fact] public void Exercises_EveryDayFilled_IsDone()
    {
        var (step, open) = SetupPlan.Exercises(Menu(Day(1, "Upper", 5), Day(2, "Lower", 3)), null);

        Assert.Equal("All 2 days have exercises", step.Result);
        Assert.Null(open);
    }

    // --- Step 3: body data, then the calorie goal ---

    private static CalorieGoalInfo Goal(int? kcal) => new(GoalType.Maintain, kcal, null, null, false, Sex.Male);

    private static ProfileSummary Profile(UserProfile p, double? weightKg, int? maintenance) =>
        new("cristi", null, null, 0, 0, p,
            weightKg is { } w ? new WeightLog { WeightKg = w, Date = AppClock.Today } : null, [],
            new MaintenanceEstimate(maintenance));

    private static UserProfile Full() => new()
    {
        Sex = Sex.Male, BirthDate = new DateOnly(2000, 1, 1), HeightCm = 180, ActivityLevel = ActivityLevel.Active
    };

    [Fact] public void Calories_MissingBodyData_AsksForItFirst_InProfile()
    {
        var (step, opensProfile) = SetupPlan.Calories(Goal(null), Profile(new UserProfile { HeightCm = 180 }, null, null));

        Assert.True(opensProfile);
        Assert.Equal(("Fill in your body data", "Open Profile"), (step.Title, step.Cta));
        Assert.Equal("Add your birth date, weight and activity in Profile. They give you a maintenance estimate to base your calorie goal on.", step.Text);
        Assert.Null(step.Result);
    }

    [Fact] public void Calories_BodyDataFilled_MovesOnToTheGoal_WithTheMaintenance()
    {
        var (step, opensProfile) = SetupPlan.Calories(Goal(null), Profile(Full(), 80, 2650));

        Assert.False(opensProfile);
        Assert.Equal(("Set your calorie goal", "Open Calories"), (step.Title, step.Cta));
        Assert.StartsWith("Your maintenance is about 2,650 kcal a day.", step.Text);
    }

    [Fact] public void Calories_NoGender_StillCountsAsFilled()
    {
        // "Prefer not to say" is stored as no gender, so it must not block the step
        var p = Full();
        p.Sex = null;

        var (step, opensProfile) = SetupPlan.Calories(Goal(null), Profile(p, 80, null));

        Assert.False(opensProfile);
        Assert.Equal("Choose lose, maintain or gain, or enter your own daily target.", step.Text);
    }

    [Fact] public void Calories_AGoalAlreadySet_IsDone_EvenWithoutBodyData()
    {
        var (step, _) = SetupPlan.Calories(Goal(2200), Profile(new UserProfile(), null, null));

        Assert.Equal("Maintain · 2,200 kcal", step.Result);
    }
}
