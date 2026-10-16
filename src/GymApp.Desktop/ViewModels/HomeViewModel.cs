using System;
using System.Globalization;
using System.Linq;
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
    private readonly PointsService _points;
    private readonly GymService _gyms;
    private readonly ProfileService _profiles;

    public HomeViewModel(SessionService session, Func<MainViewModel> shell,
                         WorkoutService workouts, StreakService streaks, CalorieService calories,
                         PointsService points, GymService gyms, ProfileService profiles)
    {
        _session = session;
        _shell = shell;
        _workouts = workouts;
        _streaks = streaks;
        _calories = calories;
        _points = points;
        _gyms = gyms;
        _profiles = profiles;
        _ = RefreshAsync();   // also recalculates the streak and syncs points, so overnight changes show correctly
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

    [ObservableProperty] public partial string LevelTitle { get; set; } = "";
    [ObservableProperty] public partial string PointsText { get; set; } = "";
    [ObservableProperty] public partial double LevelProgress { get; set; }
    [ObservableProperty] public partial string LevelHint { get; set; } = "";
    [ObservableProperty] public partial string? PointsNotice { get; set; }

    // ===== Gym (optional, after check-in; never affects points or the streak) =====
    [ObservableProperty] public partial bool ShowGymPrompt { get; set; }
    [ObservableProperty] public partial bool HasLastGym { get; set; }
    [ObservableProperty] public partial string LastGymLabel { get; set; } = "";
    [ObservableProperty] public partial bool ShowGymLink { get; set; }
    [ObservableProperty] public partial string GymLinkText { get; set; } = "";
    [ObservableProperty] public partial string? GymNotice { get; set; }

    private GymChoice? _lastGym;

    private async Task RefreshAsync()
    {
        if (_session.CurrentUser is not { } user) return;
        try
        {
            var today = await _workouts.GetTodayAsync(user.Id);
            var streak = await _streaks.RecalculateAsync(user.Id);
            var points = await _points.SyncAsync(user.Id);   // catches a week that ended while the app was closed
            Apply(today, streak);
            ApplyPoints(points);
            await RefreshGymAsync(user.Id);
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
            ({ Hit: false } t2, _) => $"Target missed: aim for {CalorieGoals.TargetRule(t2.Goal!.Value)}.",
            (_, { } goal) => $"Goal: {CalorieGoals.TargetRule(goal)}. Log what you ate at the end of the day.",
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
        ApplyPoints(r.Points);
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
            StreakStatus.AtRisk => "Work out today to keep your streak",
            StreakStatus.Shifted => "You're one day behind, so train tomorrow (or on your next rest day)",
            StreakStatus.Broken => "Streak broken. Log today's workout to start a new one.",
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
        ApplyPoints(result.Points);

        // The workout is saved; now (optionally) ask for the gym
        var gym = await RefreshGymAsync(user.Id);
        ShowGymPrompt = gym is { LoggedToday: true, Gym: null, AskAfterCheckIn: true };
    }

    /// <summary>Updates the "Add gym" / "Change gym" link under today's log. Only today's log has it, so after
    /// midnight yesterday's log is read-only.</summary>
    private async Task<TodayGymInfo?> RefreshGymAsync(int userId)
    {
        try
        {
            var info = await _gyms.GetTodayAsync(userId);
            ShowGymLink = info.LoggedToday;
            GymLinkText = info.Gym is { } g ? $"Change gym: {g.Name}" : "Add gym";
            _lastGym = info.LastUsed;
            HasLastGym = info.LastUsed is not null;
            LastGymLabel = info.LastUsed is { } last ? last.Name : "";
            return info;
        }
        catch (Exception)
        {
            ShowGymLink = false;
            return null;
        }
    }

    [RelayCommand]
    private async Task UseLastGymAsync()
    {
        if (_session.CurrentUser is not { } user || _lastGym is not { } gym) return;
        var r = await _gyms.SetGymForTodayAsync(user.Id, gym.Id);
        if (!r.Success) { Error = r.Error; return; }
        Error = null;
        ShowGymPrompt = false;
        await RefreshGymAsync(user.Id);
    }

    [RelayCommand]
    private void ChooseGym() => _shell().ShowGymPicker();

    // Skip only hides it: the prompt appears right after "I went today", which can't happen twice in a day
    [RelayCommand]
    private void SkipGymPrompt() => ShowGymPrompt = false;

    [RelayCommand]
    private async Task NeverAskForGymAsync()
    {
        if (_session.CurrentUser is not { } user) return;
        ShowGymPrompt = false;
        var r = await _profiles.SetAskForGymAfterCheckInAsync(user.Id, false);
        if (!r.Success) { Error = r.Error; return; }
        GymNotice = "Okay, no more gym questions. You can turn this back on in your profile.";
    }

    /// <summary>Updates the level card. A sync that added points shows a short notice (and "Level up" when the
    /// level rose); a drop only updates the card, without a message.</summary>
    private void ApplyPoints(PointsSync? sync)
    {
        if (sync is null) return;
        var level = sync.NewLevel;

        LevelTitle = $"Level {level.Number} · {level.Name}";
        PointsText = $"{N(sync.NewTotal)} points";
        LevelProgress = level.Progress;
        LevelHint = level.PointsToNext(sync.NewTotal) is { } left ? $"{N(left)} points to {level.NextName}" : "Max level";

        if (sync.Added.Count > 0)
        {
            var parts = sync.Added.GroupBy(a => a.Reason)
                .Select(g => $"+{N(g.Sum(a => a.Amount))} {PointsCalculator.DisplayName(g.Key)}")
                .ToList();
            if (sync.LeveledUp) parts.Add($"Level up: {level.Name}");
            PointsNotice = string.Join(" · ", parts);
        }

        if (_session.CurrentUser is { } u) u.Points = sync.NewTotal;   // keep the in-memory session user in sync
    }

    private static string N(int n) => n.ToString("N0", CultureInfo.InvariantCulture);

    [RelayCommand]
    private void ShowPointsHistory() => _shell().ShowPointsHistory();
    [RelayCommand]
    private void Logout() => _session.SignOut();
    [RelayCommand]
    private void ShowSplits() => _shell().ShowSplits();
    [RelayCommand]
    private void ShowWorkouts() => _shell().ShowWorkouts();
    [RelayCommand]
    private void ShowProfile() => _shell().ShowProfile();
    [RelayCommand]
    private void ShowGymSummary() => _shell().ShowGymSummary();
}
