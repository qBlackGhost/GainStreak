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
    private readonly FriendService _friends;
    private readonly LeaderboardService _leaderboard;
    private readonly SetService _sets;
    private readonly WorkoutMenuService _menu;
    private readonly MissedDayService _missedDays;

    public HomeViewModel(SessionService session, Func<MainViewModel> shell,
                         WorkoutService workouts, StreakService streaks, CalorieService calories,
                         PointsService points, GymService gyms, ProfileService profiles,
                         FriendService friends, LeaderboardService leaderboard,
                         SetService sets, WorkoutMenuService menu, MissedDayService missedDays)
    {
        _session = session;
        _shell = shell;
        _workouts = workouts;
        _streaks = streaks;
        _calories = calories;
        _points = points;
        _gyms = gyms;
        _profiles = profiles;
        _friends = friends;
        _leaderboard = leaderboard;
        _sets = sets;
        _menu = menu;
        _missedDays = missedDays;
        TodayWorkout = new TodayWorkoutCardViewModel(shell);
        _ = RefreshAsync();   // also recalculates the streak and syncs points, so overnight changes show correctly
    }

    public string Welcome => $"Welcome, {_session.CurrentUser?.Username}!";

    // ===== Dashboard cards =====
    public TodayWorkoutCardViewModel TodayWorkout { get; }
    public WeekCardViewModel Week { get; } = new();
    public RecentPrsCardViewModel RecentPrs { get; } = new();

    [ObservableProperty] public partial string TodayTitle { get; set; } = "";
    [ObservableProperty] public partial string TodayHint { get; set; } = "";
    [ObservableProperty] public partial string LogLabel { get; set; } = "I went today";
    [ObservableProperty] public partial bool CanLog { get; set; }
    [ObservableProperty] public partial bool CanUndo { get; set; }
    [ObservableProperty] public partial bool IsConfirmingUndo { get; set; }

    /// <summary>The check-in popup while it's open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCheckingIn))]
    public partial CheckInViewModel? CheckIn { get; set; }

    public bool IsCheckingIn => CheckIn is not null;

    [ObservableProperty] public partial string UndoConfirmText { get; set; } = "";

    /// <summary>What the last undo took away, e.g. "−5 Workout · −50 Week complete".</summary>
    [ObservableProperty] public partial string? PointsLostNotice { get; set; }
    [ObservableProperty] public partial string StreakText { get; set; } = "";
    [ObservableProperty] public partial string StreakNote { get; set; } = "";

    /// <summary>The missed-day card, while yesterday's missed workout needs a choice. Check-in waits for it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMissedDay))]
    public partial MissedDayViewModel? MissedDay { get; set; }

    public bool HasMissedDay => MissedDay is not null;

    /// <summary>"Your streak ended on Tuesday, 6 Oct." Shown once, on the first refresh after the streak broke.</summary>
    [ObservableProperty] public partial string? StreakEndedNotice { get; set; }
    [ObservableProperty] public partial string CaloriesTitle { get; set; } = "";
    [ObservableProperty] public partial string CaloriesHint { get; set; } = "";
    [ObservableProperty] public partial string? GoalReviewHint { get; set; }
    [ObservableProperty] public partial string CaloriesInput { get; set; } = "";
    [ObservableProperty] public partial string LogCaloriesLabel { get; set; } = "Log";
    [ObservableProperty] public partial string? CaloriesError { get; set; }
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

    // ===== Friends card =====
    [ObservableProperty] public partial string FriendsTitle { get; set; } = "Add friends";
    [ObservableProperty] public partial bool HasFriends { get; set; }
    [ObservableProperty] public partial int IncomingRequests { get; set; }
    [ObservableProperty] public partial bool HasIncomingRequests { get; set; }

    private GymChoice? _lastGym;

    private async Task RefreshAsync()
    {
        if (_session.CurrentUser is not { } user) return;
        try
        {
            var cached = user.CurrentStreak;   // before the recalculation, to notice a streak that broke overnight
            var today = await _workouts.GetTodayAsync(user.Id);
            var streak = await _streaks.RecalculateAsync(user.Id);
            var points = await _points.SyncAsync(user.Id);   // catches a week that ended while the app was closed
            await RefreshMissedDayAsync(user.Id);
            Apply(today, streak);
            await RefreshStreakEndedAsync(user.Id, cached, streak);
            await RefreshDashboardAsync(user.Id);
            ApplyPoints(points);
            await RefreshGymAsync(user.Id);
            await RefreshCaloriesAsync(user.Id);
        }
        catch (Exception)
        {
            Error = "Could not load today's workout.";
        }
        await RefreshFriendsAsync(user.Id);
    }

    /// <summary>Today's workout, this week's row and recent PRs. They change with a check-in, an undo or a missed-day
    /// choice, so those refresh them too. A failure only empties the cards.</summary>
    private async Task RefreshDashboardAsync(int userId)
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            TodayWorkout.Apply(await _workouts.GetTodayWorkoutAsync(userId, today));
            Week.Apply(await _streaks.GetWeekAsync(userId, today));
            RecentPrs.Apply(await _sets.GetRecentPrsAsync(userId));
        }
        catch (Exception)
        {
            Error = "Could not load your dashboard.";
        }
    }

    private async Task RefreshMissedDayAsync(int userId)
    {
        var prompt = await _missedDays.GetPromptAsync(userId);
        MissedDay = prompt is null ? null : new MissedDayViewModel(_missedDays, userId, prompt, ChoseAsync);
    }

    private async Task RefreshStreakEndedAsync(int userId, int cached, StreakResult streak)
    {
        StreakEndedNotice = null;
        if (cached == 0 || streak.Status != StreakStatus.Broken) return;
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (await _streaks.LastBreakAsync(userId, today) is { } missed)
            StreakEndedNotice = $"Your streak ended on {missed.ToString("dddd, d MMM", CultureInfo.InvariantCulture)}.";
    }

    /// <summary>After a missed-day choice: the card goes away and today's workout and the streak update.</summary>
    private async Task ChoseAsync(MissedDayResult result)
    {
        try
        {
            if (_session.CurrentUser is not { } user) return;
            MissedDay = null;
            Error = null;
            Apply(await _workouts.GetTodayAsync(user.Id), result.Streak!);
            ApplyPoints(result.Points);
            await RefreshDashboardAsync(user.Id);
            await RefreshFriendsAsync(user.Id);

        }
        catch (Exception)
        {
            Error = "Your choice was saved, but Home couldn't refresh. Reopen Home to see it.";
        }
    }

    /// <summary>"#2 of 5 this week" and the incoming-requests badge. Runs after the user's own sync, so their
    /// weekly points are already current. A failure only hides the card's details.</summary>
    private async Task RefreshFriendsAsync(int userId)
    {
        try
        {
            IncomingRequests = await _friends.IncomingCountAsync(userId);
            HasIncomingRequests = IncomingRequests > 0;

            var board = await _leaderboard.GetAsync(userId, LeaderboardPeriod.ThisWeek);
            HasFriends = board is { IsAlone: false };
            FriendsTitle = board is { IsAlone: false, You: { } you }
                ? $"#{you.Rank} of {board.Rows.Count} this week"
                : "Add friends";
        }
        catch (Exception)
        {
            HasFriends = false;
            FriendsTitle = "Friends";
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
        try
        {
            if (_session.CurrentUser is not { } user) return;
            CaloriesError = null;
            if (!NumberParsing.TryParseWhole(CaloriesInput, out var kcal))
            {
                CaloriesError = "Enter what you ate today as a whole number of kcal, e.g. 2100.";
                return;
            }

            var r = await _calories.LogTodayAsync(user.Id, kcal);
            if (!r.Success) { CaloriesError = r.Error; return; }

            CaloriesInput = "";
            ApplyPoints(r.Points);
            await RefreshCaloriesAsync(user.Id);

        }
        catch (Exception)
        {
            CaloriesError = "Could not save your calories.";
        }
    }

    private void Apply(TodayInfo today, StreakResult streak)
    {
        TodayTitle = !today.HasSplit ? "No split selected" : today.Title;
        TodayHint = !today.HasSplit ? "Pick a split to get a weekly schedule."
                  : today.Logged ? "Workout logged ✓"
                  : HasMissedDay ? WorkoutService.NeedsChoiceError
                  : today.IsTraining ? "Workout still needed today."
                  : "Rest day. You can still log an extra workout.";
        LogLabel = today.Logged ? "Logged ✓" : "I went today";
        CanLog = !today.Logged && !HasMissedDay;
        CanUndo = today.Logged;   // today only: after midnight yesterday's log is read-only
        if (!today.Logged) IsConfirmingUndo = false;

        StreakText = $"{streak.Current} day streak · best {streak.Longest}";
        StreakNote = streak.Status switch
        {
            StreakStatus.NotStarted => "Log your first workout to start a streak.",
            StreakStatus.Active => "Streak alive, nice.",
            StreakStatus.PendingToday => "Log today's workout to keep your streak.",
            StreakStatus.NeedsChoice => "You missed yesterday. Choose what to do to keep your streak.",
            StreakStatus.AtRisk => today.HasSplit ? $"Do {today.Title} today to keep your streak."
                                                  : "Work out today to keep your streak.",
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

    /// <summary>"I went today" opens the check-in popup; the popup saves the sets and logs the workout.</summary>
    [RelayCommand]
    private void LogToday()
    {
        if (!CanLog || _session.CurrentUser is not { } user) return;
        Error = null;
        CheckIn = new CheckInViewModel(_workouts, _sets, _menu, _shell, user.Id, r => _ = CheckedInAsync(r),
            () => CheckIn = null);
    }

    private async Task CheckedInAsync(LogResult result)
    {
        try
        {
            CheckIn = null;
            if (_session.CurrentUser is not { } user) return;
            Error = null;
            PointsLostNotice = null;
            Apply(await _workouts.GetTodayAsync(user.Id), result.Streak!);
            ApplyPoints(result.Points);

            // The workout is saved; now (optionally) ask for the gym
            var gym = await RefreshGymAsync(user.Id);
            ShowGymPrompt = gym is { LoggedToday: true, Gym: null, AskAfterCheckIn: true };
            await RefreshDashboardAsync(user.Id);
            await RefreshFriendsAsync(user.Id);

        }
        catch (Exception)
        {
            Error = "Your workout was saved, but Home couldn't refresh. Reopen Home to see it.";
        }
    }

    /// <summary>Names everything today's check-in earned (the +5, and a week bonus when it completed the week).</summary>
    [RelayCommand]
    private async Task StartUndoAsync()
    {
        var earned = "today's +5";
        if (_session.CurrentUser is { } user)
        {
            try
            {
                var today = DateOnly.FromDateTime(DateTime.Now);
                var lines = (await _points.GetAsync(user.Id))?.History.Where(l => l.Date == today).ToList() ?? [];
                if (lines.Count > 0)
                    earned = "today's " + string.Join(" and ", lines.GroupBy(l => l.Reason)
                        .Select(g => $"+{N(g.Sum(l => l.Amount))} {PointsCalculator.DisplayName(g.Key)}"));
            }
            catch (Exception)
            {
                // keep the generic text
            }
        }
        UndoConfirmText = $"Undo today's check-in? You'll lose {earned} points and your streak goes back.";
        IsConfirmingUndo = true;
    }
    [RelayCommand] private void CancelUndo() => IsConfirmingUndo = false;

    [RelayCommand]
    private async Task ConfirmUndoAsync()
    {
        IsConfirmingUndo = false;
        if (_session.CurrentUser is not { } user) return;
        try
        {
            var result = await _workouts.UndoTodayAsync(user.Id);
            if (!result.Success) { Error = result.Error; return; }

            Error = null;
            PointsNotice = null;
            GymNotice = null;
            PointsLostNotice = result.Points is { Removed.Count: > 0 } sync
                ? string.Join(" · ", sync.Removed.GroupBy(a => a.Reason)
                    .Select(g => $"−{N(g.Sum(a => a.Amount))} {PointsCalculator.DisplayName(g.Key)}"))
                : null;
            ShowGymPrompt = false;
            Apply(await _workouts.GetTodayAsync(user.Id), result.Streak!);
            user.LongestStreak = result.Streak!.Longest;   // undo may lower the best streak
            ApplyPoints(result.Points);
            await RefreshGymAsync(user.Id);
            await RefreshDashboardAsync(user.Id);
        }
        catch (Exception)
        {
            Error = "Could not undo today's check-in.";
        }
        await RefreshFriendsAsync(user.Id);
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
        try
        {
            if (_session.CurrentUser is not { } user || _lastGym is not { } gym) return;
            var r = await _gyms.SetGymForTodayAsync(user.Id, gym.Id);
            if (!r.Success) { Error = r.Error; return; }
            Error = null;
            ShowGymPrompt = false;
            await RefreshGymAsync(user.Id);

        }
        catch (Exception)
        {
            Error = "Could not save the gym.";
        }
    }

    [RelayCommand]
    private void ChooseGym() => _shell().ShowGymPicker();

    // Skip only hides it: the prompt appears right after "I went today", which can't happen twice in a day
    [RelayCommand]
    private void SkipGymPrompt() => ShowGymPrompt = false;

    [RelayCommand]
    private async Task NeverAskForGymAsync()
    {
        try
        {
            if (_session.CurrentUser is not { } user) return;
            ShowGymPrompt = false;
            var r = await _profiles.SetAskForGymAfterCheckInAsync(user.Id, false);
            if (!r.Success) { Error = r.Error; return; }
            GymNotice = "Okay, no more gym questions. You can turn this back on in your profile.";

        }
        catch (Exception)
        {
            Error = "Could not save that setting.";
        }
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
    [RelayCommand]
    private void ShowFriends() => _shell().ShowFriends();
    [RelayCommand]
    private void ShowLeaderboard() => _shell().ShowLeaderboard();
}
