using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Core;
using GymApp.Services;

namespace GymApp.Desktop.ViewModels;

public partial class HomeViewModel : ViewModelBase
{
    private readonly SessionService _session;
    private readonly Func<MainViewModel> _shell;
    private readonly WorkoutService _workouts;
    private readonly StreakService _streaks;
    private readonly CalorieService _calories;

    public HomeViewModel(SessionService session, Func<MainViewModel> shell,
                         WorkoutService workouts, StreakService streaks, CalorieService calories)
    {
        _session = session;
        _shell = shell;
        _workouts = workouts;
        _streaks = streaks;
        _calories = calories;
        _ = RefreshAsync();   // also recalculates the streak, so one that broke overnight shows correctly
    }

    public string Welcome => $"Welcome, {_session.CurrentUser?.Username}!";

    [ObservableProperty] public partial string TodayTitle { get; set; } = "";
    [ObservableProperty] public partial string TodayHint { get; set; } = "";
    [ObservableProperty] public partial string LogLabel { get; set; } = "I went today";
    [ObservableProperty] public partial bool CanLog { get; set; }
    [ObservableProperty] public partial string StreakText { get; set; } = "";
    [ObservableProperty] public partial string StreakNote { get; set; } = "";
    [ObservableProperty] public partial string CaloriesTitle { get; set; } = "";
    [ObservableProperty] public partial string CaloriesHint { get; set; } = "";
    [ObservableProperty] public partial string? GoalReviewHint { get; set; }
    [ObservableProperty] public partial string CaloriesInput { get; set; } = "";
    [ObservableProperty] public partial string LogCaloriesLabel { get; set; } = "Log";
    [ObservableProperty] public partial string? Error { get; set; }

    private async Task RefreshAsync()
    {
        if (_session.CurrentUser is not { } user) return;
        try
        {
            var today = await _workouts.GetTodayAsync(user.Id);
            var streak = await _streaks.RecalculateAsync(user.Id);
            Apply(today, streak);
            await RefreshCaloriesAsync(user.Id);
        }
        catch (Exception)
        {
            Error = "Could not load today's workout.";
        }
    }

    private async Task RefreshCaloriesAsync(int userId)
    {
        var o = await _calories.GetAsync(userId);
        if (o is null) return;
        var g = o.Goal;

        CaloriesTitle = o.Today is { } t ? $"{CalorieGoals.K(t.Consumed)} kcal" : "Not logged yet";
        CaloriesHint = (o.Today, g.Goal) switch
        {
            ({ Hit: true }, _) => "Target hit ✓",
            ({ Hit: false } t2, _) => $"Target missed: aim for {CalorieGoals.TargetRule(t2.GoalType!.Value, t2.Goal!.Value)}.",
            (_, { } goal) => $"Goal: {CalorieGoals.TargetRule(g.GoalType, goal)}. Log what you ate at the end of the day.",
            _ => "No calorie goal yet. Set one on your profile."
        };
        GoalReviewHint = g.SuggestionChanged ? "Your suggested goal changed. Review it on your profile." : null;
        LogCaloriesLabel = o.Today is null ? "Log" : "Update";
    }

    [RelayCommand]
    private async Task LogCaloriesAsync()
    {
        if (_session.CurrentUser is not { } user) return;
        if (!Units.TryParseWholeNumber(CaloriesInput, out var kcal))
        {
            Error = "Enter what you ate today as a whole number of kcal, e.g. 2100.";
            return;
        }

        var r = await _calories.LogTodayAsync(user.Id, kcal);
        if (!r.Success) { Error = r.Error; return; }

        Error = null;
        CaloriesInput = "";
        await RefreshCaloriesAsync(user.Id);
    }

    private void Apply(TodayInfo today, StreakResult streak)
    {
        TodayTitle = !today.HasSplit ? "No split selected" : today.Title;
        TodayHint = !today.HasSplit ? "Pick a split to get a weekly schedule."
                  : today.Logged ? "Workout logged ✓"
                  : today.IsTraining ? "Workout still needed today."
                  : "Rest day. You can still log an extra workout.";
        LogLabel = today.Logged ? "Logged ✓" : "I went today";
        CanLog = !today.Logged;

        StreakText = $"{streak.Current} day streak · best {streak.Longest}";
        StreakNote = streak.Status switch
        {
            StreakStatus.NotStarted => "Log your first workout to start a streak.",
            StreakStatus.Active => "Streak alive, nice.",
            StreakStatus.PendingToday => "Log today's workout to keep your streak.",
            StreakStatus.AtRisk => "Work out today to save your streak.",
            StreakStatus.Broken => "Streak broken. Your next workout starts a new one.",
            _ => ""
        };

        // keep the in-memory session user in sync
        if (_session.CurrentUser is { } u)
        {
            u.CurrentStreak = streak.Current;
            u.LongestStreak = Math.Max(u.LongestStreak, streak.Longest);
        }
    }

    [RelayCommand]
    private async Task LogTodayAsync()
    {
        if (!CanLog || _session.CurrentUser is not { } user) return;

        var result = await _workouts.LogTodayAsync(user.Id);
        if (!result.Success) { Error = result.Error; return; }

        Error = null;
        Apply(await _workouts.GetTodayAsync(user.Id), result.Streak!);
    }

    [RelayCommand]
    private void Logout() => _session.SignOut();
    [RelayCommand]
    private void ShowSplits() => _shell().ShowSplits();
    [RelayCommand]
    private void ShowWorkouts() => _shell().ShowWorkouts();
    [RelayCommand]
    private void ShowProfile() => _shell().ShowProfile();
}
