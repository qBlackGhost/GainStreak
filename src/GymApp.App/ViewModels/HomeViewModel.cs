using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Core;
using GymApp.Services;
using GymApp.Social;

namespace GymApp.App.ViewModels;

public partial class HomeViewModel : ViewModelBase, IClosesOnBack
{
    /// <summary>Back closes the check-in, the live workout's sheets, the undo question or the points history first.</summary>
    public bool CloseOnBack()
    {
        if (IsCheckingIn) { CheckIn = null; return true; }
        if (Picker is not null) { Picker = null; return true; }
        if (IsLocationNoteOpen) { AnswerLocationNote(false); return true; }
        if (IsReturnOpen) { _ = ContinueWorkoutAsync(); return true; }
        if (Vacation.CloseOnBack()) return true;
        if (IsCardioPromptOpen) { SkipCardio(); return true; }
        if (Live?.CloseOnBack() == true) return true;
        if (IsConfirmingUndo) { IsConfirmingUndo = false; return true; }
        if (IsPointsHistoryOpen) { IsPointsHistoryOpen = false; return true; }
        return false;
    }

    private readonly SessionService _session;
    private readonly Func<MainViewModel> _shell;
    private readonly WorkoutService _workouts;
    private readonly StreakService _streaks;
    private readonly CalorieService _calories;
    private readonly PointsService _points;
    private readonly GymService _gyms;
    private readonly ProfileService _profiles;
    private readonly ILeaderboardService _leaderboard;
    private readonly SetService _sets;
    private readonly WorkoutMenuService _menu;
    private readonly MissedDayService _missedDays;
    private readonly GymSummaryService _gymSummary;
    private readonly LiveWorkoutService _liveWorkouts;
    private readonly LiveWorkoutWatcher _watcher;
    private readonly INotificationScheduler _notifications;
    private readonly ILocationProvider _location;
    private readonly CardioService _cardio;
    private readonly VacationService _vacations;
    private readonly AwayWatcher _away;

    public HomeViewModel(SessionService session, Func<MainViewModel> shell,
                         WorkoutService workouts, StreakService streaks, CalorieService calories,
                         PointsService points, GymService gyms, ProfileService profiles,
                         ILeaderboardService leaderboard,
                         SetService sets, WorkoutMenuService menu, MissedDayService missedDays,
                         GymSummaryService gymSummary, IAccountService accounts, PointsHistoryViewModel pointsHistory,
                         InboxService inbox, IPlatform platform, LiveWorkoutService liveWorkouts,
                         LiveWorkoutWatcher watcher, INotificationScheduler notifications, ILocationProvider location,
                         CardioService cardio, VacationService vacations, AwayWatcher away)
    {
        _vacations = vacations;
        _away = away;
        Vacation = new VacationCardViewModel(vacations, () => _session.CurrentUser?.Id, VacationChangedAsync);
        _location = location;
        _cardio = cardio;
        _inbox = inbox;
        _liveWorkouts = liveWorkouts;
        _watcher = watcher;
        _notifications = notifications;
        // The return popup and the 4-hour / midnight stop (18b) come from the watcher, on any thread
        watcher.Listen(() => Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = RefreshAsync()));
        // Back from a locked phone: the cardio timer checks the gym straight away (0.18.2)
        watcher.ListenForeground(() => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (Cardio is { } c) _ = c.CheckNowAsync();
            _ = CheckAwayAsync();   // back in front: maybe on a trip now (19c)
        }));
        HasLiveWorkout = platform.HasLiveWorkout;
        HasFoodLog = platform.HasFoodLog;
        PointsHistory = pointsHistory;
        _session = session;
        _accounts = accounts;
        _shell = shell;
        _workouts = workouts;
        _streaks = streaks;
        _calories = calories;
        _points = points;
        _gyms = gyms;
        _profiles = profiles;
        _leaderboard = leaderboard;
        _sets = sets;
        _menu = menu;
        _missedDays = missedDays;
        _gymSummary = gymSummary;
        TodayWorkout = new TodayWorkoutCardViewModel(shell, canVerify: HasLiveWorkout);
        _loaded = RefreshAsync();   // also recalculates the streak and syncs points, so overnight changes show correctly
    }

    private readonly Task _loaded;

    /// <summary>The "Start workout" control (Phase 24): once Home has loaded, starts today's workout as the button would
    /// (a rest day opens the exercise picker). Does nothing when the button isn't offered (logged, live, a missed day).</summary>
    public async Task StartWhenLoadedAsync()
    {
        await _loaded;
        if (CanStart && !IsLive && !IsCardio) await StartAsync(null);
    }

    private readonly IAccountService _accounts;
    private readonly InboxService _inbox;

    public string Username => _session.CurrentUser?.Username ?? "";

    /// <summary>"You're offline. Last updated 14:05." under the friends card, when the board came from the cache.</summary>
    [ObservableProperty] public partial string? FriendsStale { get; set; }

    /// <summary>Signed in with the cached password: the server couldn't be reached.</summary>
    public string? OfflineNotice => _session.SignedInOffline ? "You're offline. Friends will update when you're back online." : null;
    public string Initials => UserInitials.Of(Username);
    public string DateText => AppClock.Now.ToString("dddd, d MMMM", CultureInfo.InvariantCulture);
    public string Greeting => AppClock.Now.Hour switch
    {
        < 12 => $"Good morning, {Username}",
        < 18 => $"Good afternoon, {Username}",
        _ => $"Good evening, {Username}"
    };

    // ===== Streak hero: the big number, and today's gauge (cold until checked in, then on fire) =====
    [ObservableProperty] public partial int StreakCurrent { get; set; }
    [ObservableProperty] public partial string BestStreakText { get; set; } = "";
    [ObservableProperty] public partial string GymsThisMonth { get; set; } = "–";
    [ObservableProperty] public partial bool IsDone { get; set; }
    [ObservableProperty] public partial bool IsRestDay { get; set; }
    [ObservableProperty] public partial bool IsTrainingOpen { get; set; }
    [ObservableProperty] public partial bool IsRestOpen { get; set; }
    [ObservableProperty] public partial string GaugeKicker { get; set; } = "TODAY";
    [ObservableProperty] public partial bool IsMakeUpDay { get; set; }
    [ObservableProperty] public partial string GaugeTitle { get; set; } = "";
    [ObservableProperty] public partial string GaugeSub { get; set; } = "";
    [ObservableProperty] public partial string StatusText { get; set; } = "";

    /// <summary>Sets were logged today, but no check-in yet.</summary>
    [ObservableProperty] public partial bool ShowSetsHint { get; set; }

    // ===== Live workout (Phase 18, phones): Start workout, the card under the streak card, focus mode =====

    /// <summary>The head logs through the live workout instead of "I went today" (phones; the TestBench can switch).</summary>
    public bool HasLiveWorkout { get; }
    public bool HasCheckIn => !HasLiveWorkout;

    /// <summary>The open session's card. While it's open Home is in focus mode: only the streak card and the card.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLive), nameof(ShowDashboard))]
    public partial LiveWorkoutCardViewModel? Live { get; set; }

    /// <summary>The iPhone's Live Activity follows the card: shown while it's open, ended when it goes (0.20.0).</summary>
    partial void OnLiveChanged(LiveWorkoutCardViewModel? value) => _watcher.ShowActivity(value?.Info);

    public bool IsLive => Live is not null;
    public bool ShowDashboard => !IsLive;

    // ===== Cardio after a workout (0.18.2, phones): the prompt after Finish, then the timer card =====

    /// <summary>The open cardio timer's card, under the streak card. Home stays as it is around it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCardio))]
    public partial CardioCardViewModel? Cardio { get; set; }

    public bool IsCardio => Cardio is not null;

    /// <summary>"Continue with a cardio session?", right after Finish.</summary>
    [ObservableProperty] public partial bool IsCardioPromptOpen { get; set; }
    [ObservableProperty] public partial string? CardioPromptError { get; set; }

    private CardioCardViewModel CardioCard(int userId, CardioInfo info) =>
        new(_cardio, _location, userId, info, CardioEndedAsync);

    [RelayCommand]
    private async Task StartCardioAsync()
    {
        if (_session.CurrentUser is not { } user) return;
        CardioPromptError = null;
        try
        {
            var (info, error) = await _cardio.StartAsync(user.Id);
            if (info is null) { CardioPromptError = error ?? "Could not start the timer."; return; }
            IsCardioPromptOpen = false;
            Cardio = CardioCard(user.Id, info);
            CanUndo = false;   // the minutes go onto today's log
        }
        catch (Exception)
        {
            CardioPromptError = "Could not start the timer.";
        }
    }

    [RelayCommand]
    private void SkipCardio()
    {
        IsCardioPromptOpen = false;
        CardioPromptError = null;
    }

    /// <summary>Finish cardio, or the timer stopped by itself: the card goes and Home shows the day's points with it.</summary>
    private async Task CardioEndedAsync(CardioEndResult? result)
    {
        if (Cardio is null) return;
        Cardio = null;
        if (result is { Log.Points: { } points }) ApplyPoints(points);
        await RefreshAsync();
    }

    /// <summary>The arc fills with the share of exercises done (phones, during and after a live workout).</summary>
    [ObservableProperty] public partial bool ShowProgressArc { get; set; }
    [ObservableProperty] public partial double ProgressSweep { get; set; }
    /// <summary>Some of the arc is on fire: the cold dashes go and the day's name glows.</summary>
    [ObservableProperty] public partial bool IsArcHot { get; set; }
    [ObservableProperty] public partial bool ShowColdArc { get; set; }
    [ObservableProperty] public partial bool ShowFireArc { get; set; }
    [ObservableProperty] public partial bool IsTitleHot { get; set; }
    /// <summary>Today is a rest day that didn't add to the streak (0.18.1): the streak is kept but frozen, so the flame,
    /// the number and the arc turn to ice instead of fire. Not during a live workout.</summary>
    [ObservableProperty] public partial bool IsStreakStale { get; set; }
    [ObservableProperty] public partial bool ShowRestArc { get; set; }

    /// <summary>Phones before a workout: Start workout (or why it can't start yet), and after one: the result and Undo.</summary>
    [ObservableProperty] public partial bool ShowStartRow { get; set; }
    [ObservableProperty] public partial bool ShowDoneRow { get; set; }
    [ObservableProperty] public partial bool CanStart { get; set; }
    [ObservableProperty] public partial string StartLabel { get; set; } = "Start workout";
    [ObservableProperty] public partial bool ShowPickSplit { get; set; }
    [ObservableProperty] public partial bool ShowBuildWorkout { get; set; }
    [ObservableProperty] public partial string? ResultText { get; set; }

    /// <summary>"Workout ended without logging": after End workout below half, until the next workout starts.</summary>
    [ObservableProperty] public partial bool ShowEndedNote { get; set; }

    /// <summary>"Continue your workout?" after 12 minutes or more away (18b).</summary>
    [ObservableProperty] public partial bool IsReturnOpen { get; set; }
    [ObservableProperty] public partial string ReturnText { get; set; } = "";
    /// <summary>What finishing now means: no points below half, half points below 70%. Shown before choosing.</summary>
    [ObservableProperty] public partial string? ReturnWarning { get; set; }
    [ObservableProperty] public partial bool IsReturnWarningBad { get; set; }

    /// <summary>"Your workout stopped after 4 hours. …": once, the next time Home shows after a session ended by itself.</summary>
    [ObservableProperty] public partial string? StopNoteText { get; set; }
    [ObservableProperty] public partial bool IsStopNoteBad { get; set; }

    /// <summary>A rest day's Start workout anyway (0.18.1): the sheet to pick any exercises, while it's open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPicking))]
    public partial RestDayPickerViewModel? Picker { get; set; }

    public bool IsPicking => Picker is not null;

    // ===== Level pill =====
    [ObservableProperty] public partial int LevelNumber { get; set; } = 1;

    // ===== Calories card =====
    [ObservableProperty] public partial string? CalorieGoalType { get; set; }
    [ObservableProperty] public partial string CaloriesCenter { get; set; } = "—";
    [ObservableProperty] public partial string CaloriesCenterSub { get; set; } = "not logged";
    [ObservableProperty] public partial double CaloriesSweep { get; set; }
    [ObservableProperty] public partial bool CaloriesHit { get; set; }
    [ObservableProperty] public partial string? CaloriesTip { get; set; }
    [ObservableProperty] public partial string CaloriesResult { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCaloriesForm), nameof(ShowCaloriesResult))]
    public partial bool IsCaloriesLogged { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCaloriesForm), nameof(ShowCaloriesResult))]
    public partial bool IsEditingCalories { get; set; }

    /// <summary>The desktop's one-box form. On the phones (Phase 26) food is logged on the Add food sheet instead.</summary>
    public bool ShowCaloriesForm => !HasFoodLog && (!IsCaloriesLogged || IsEditingCalories);

    /// <summary>The "On target · +10 pts" row: after logging; on the phones whenever there is something logged.</summary>
    public bool ShowCaloriesResult => HasFoodLog ? IsCaloriesLogged : !ShowCaloriesForm;

    /// <summary>Phones: the card's button is Add food, which opens the sheet.</summary>
    public bool HasFoodLog { get; }

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

    /// <summary>What undoing takes away, e.g. "−5 Workout · −50 Week complete".</summary>
    [ObservableProperty] public partial string UndoLossText { get; set; } = "";

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

    // ===== Get set up (design: FirstTime board); shown until all four steps are done =====
    [ObservableProperty] public partial IReadOnlyList<SetupStepViewModel> SetupSteps { get; set; } = [];
    [ObservableProperty] public partial bool ShowSetup { get; set; }
    [ObservableProperty] public partial string SetupDoneText { get; set; } = "";
    [ObservableProperty] public partial double SetupProgress { get; set; }
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
    [ObservableProperty] public partial string? TodayGymName { get; set; }
    [ObservableProperty] public partial bool ShowAddGym { get; set; }
    [ObservableProperty] public partial bool DontAskForGym { get; set; }

    /// <summary>No gym of your own yet: the prompt offers to make today's gym yours (checked the first time).</summary>
    [ObservableProperty] public partial bool ShowMakeMyGym { get; set; }
    [ObservableProperty] public partial bool MakeMyGym { get; set; } = true;

    /// <summary>Asked once, after the first check-in with a gym: share check-ins with friends?</summary>
    [ObservableProperty] public partial bool ShowShareQuestion { get; set; }
    private bool? _shareGymCheckIns;

    // ===== Friends card =====
    [ObservableProperty] public partial string FriendsTitle { get; set; } = "Add friends";
    [ObservableProperty] public partial bool HasFriends { get; set; }
    [ObservableProperty] public partial IReadOnlyList<BoardRowViewModel> Board { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsThisWeek))]
    public partial bool IsAllTime { get; set; }

    public bool IsThisWeek => !IsAllTime;

    /// <summary>Rows shown on the dashboard; the full board is one click away.</summary>
    private const int BoardRows = 5;

    private GymChoice? _lastGym;
    private TodayInfo? _today;
    private StreakResult? _streak;
    private DateOnly? _makeUpFor;

    // ===== Vacation (0.19.0) =====

    /// <summary>Where vacation mode stands, I'm back and its question; in 19c also the away prompt.</summary>
    public VacationCardViewModel Vacation { get; }

    /// <summary>Today is a vacation day: the streak card says so (FROZEN, back on ...).</summary>
    [ObservableProperty] public partial bool IsOnVacation { get; set; }

    /// <summary>"I'm back" under the streak card: a vacation in progress or starting tomorrow, no live workout.</summary>
    [ObservableProperty] public partial bool ShowImBack { get; set; }

    /// <summary>"Welcome back!", "Vacation ended…" or "Vacation cancelled.", until dismissed.</summary>
    [ObservableProperty] public partial string? VacationNote { get; set; }

    [RelayCommand] private void DismissVacationNote() => VacationNote = null;

    /// <summary>On vacation, on phones: "Show me gyms near me" leads the streak card (19d, approved on the canvas).</summary>
    [ObservableProperty] public partial bool ShowNearbyGyms { get; set; }

    [RelayCommand] private void ShowGymsNearMe() => _shell().ShowGyms(nearMe: true);

    /// <summary>The home city, while Home asks "Away from …? On vacation?" (19c).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAwayPrompt), nameof(AwayTitle))]
    public partial string? AwayCity { get; set; }

    public bool ShowAwayPrompt => AwayCity is not null && Live is null && !Vacation.HasVacation;
    public string AwayTitle => $"Away from {AwayCity}?";

    /// <summary>A missed day must be settled first: the card says so and offers only Not now.</summary>
    public bool IsAwayBlocked => Vacation.RefusalText is not null;

    /// <summary>Looks for a trip when it's due (at most every 6 hours, phones only) and shows the prompt.</summary>
    private async Task CheckAwayAsync()
    {
        if (_session.CurrentUser is not { } user) return;
        try
        {
            var city = await _away.CheckAsync(user.Id);
            if (city is not null) await Vacation.LoadAsync();   // whether it can start, or a missed day comes first
            ApplyAway(city);
        }
        catch (Exception) { /* no prompt this time */ }
    }

    private void ApplyAway(string? city)
    {
        AwayCity = city;
        OnPropertyChanged(nameof(IsAwayBlocked));
        OnPropertyChanged(nameof(ShowAwayPrompt));
    }

    [RelayCommand] private Task AwayYesAsync() => Vacation.PlanCommand.ExecuteAsync(null);

    [RelayCommand]
    private async Task AwayNoAsync()
    {
        if (_session.CurrentUser is not { } user) return;
        ApplyAway(null);
        await _away.DismissAsync(user.Id);
    }

    [RelayCommand]
    private void AwayNotNow()
    {
        if (_session.CurrentUser is { } user) _away.Clear(user.Id);
        ApplyAway(null);
    }

    private async Task VacationChangedAsync()
    {
        if (_session.CurrentUser is { } u && Vacation.HasVacation) { _away.Clear(u.Id); ApplyAway(null); }
        VacationNote = Vacation.LastChange;
        await RefreshAsync();
        await _shell().RefreshStreakAsync();
    }

    private async Task RefreshAsync()
    {
        if (_session.CurrentUser is not { } user) return;
        try
        {
            await Vacation.LoadAsync();
            ApplyAway(_away.Pending(user.Id));
            if (await _vacations.TakeWelcomeBackAsync(user.Id)) VacationNote = "Welcome back! Your streak counts again from today.";
            var cached = user.CurrentStreak;   // before the recalculation, to notice a streak that broke overnight
            var today = await _workouts.GetTodayAsync(user.Id);
            var streak = await _streaks.RecalculateAsync(user.Id);
            var points = await _points.SyncAsync(user.Id);   // catches a week that ended while the app was closed
            await RefreshMissedDayAsync(user.Id);
            _makeUpFor = await _streaks.MakeUpDayAsync(user.Id, today.Date);
            var live = HasLiveWorkout ? await _liveWorkouts.GetOpenAsync(user.Id) : null;
            Live = live is null ? null : Card(user.Id, live);
            var cardio = HasLiveWorkout && live is null ? await _cardio.GetOpenAsync(user.Id) : null;
            Cardio = cardio is null ? null : CardioCard(user.Id, cardio);
            // Logged silently at half: the streak card shows the streak without today's log until the workout ends
            Apply(today, live is { Logged: true } ? await _streaks.GetAsync(user.Id, today.Date, beforeTodaysLog: true) : streak);
            await RefreshStreakEndedAsync(user.Id, cached, streak);
            await RefreshDashboardAsync(user.Id);
            ApplyPoints(points);
            await RefreshGymAsync(user.Id);
            await RefreshCaloriesAsync(user.Id);
            await RefreshSetupAsync(user.Id);
            await RefreshStartAsync(user.Id);
            await RefreshStopNoteAsync(user.Id);
            ShowReturnIfPending();
        }
        catch (Exception)
        {
            Error = "Could not load today's workout.";
        }
        await RefreshFriendsAsync(user.Id);
        await CheckAwayAsync();
    }

    /// <summary>The one-time note about a session that stopped by itself.</summary>
    private async Task RefreshStopNoteAsync(int userId)
    {
        if (!HasLiveWorkout || await _liveWorkouts.GetStopNoteAsync(userId) is not { } note) return;
        var lead = note.AtMidnight ? "Your workout stopped at midnight." : "Your workout stopped after 4 hours.";
        var done = $"You did {note.Filled} of {note.Total} exercise{(note.Total == 1 ? "" : "s")}";
        StopNoteText = !note.Counted
            ? $"{lead} It didn't count: fewer than half the exercises were filled. Finish a workout "
              + (note.AtMidnight ? "today" : "today or tomorrow") + " to keep your streak."
            : note.WorkoutPoints >= PointsCalculator.WorkoutPoints ? $"{lead} {done}: +{note.WorkoutPoints} pts."
            : $"{lead} {done}: half points, +{note.WorkoutPoints} pts.";
        IsStopNoteBad = !note.Counted;
        await _liveWorkouts.MarkStopNoteSeenAsync(userId, note.Id);
    }

    /// <summary>The return popup, when the watcher says the user was away 12 minutes or more.</summary>
    private void ShowReturnIfPending()
    {
        if (!_watcher.ReturnPending) return;
        if (Live is not { } card) { _watcher.ClearReturn(); return; }
        var next = card.NextExerciseName;
        ReturnText = $"{card.Filled} of {card.Total} exercises done." + (next is null ? "" : $" {next} is next.");
        var toFull = CheckInRule.RequiredForFull(card.Total) - card.Filled;
        IsReturnWarningBad = !card.Info.Status.CanLog;
        // Half points only matter for a verified workout: an unverified one earns the same either way
        ReturnWarning = IsReturnWarningBad
            ? "Finishing now won't count: fewer than half the exercises are done, so it earns no points. Your sets are "
              + "kept. Your streak is at risk unless you finish a workout today or tomorrow."
            : toFull > 0 && card.Info.IsVerified ? $"Finishing now earns half points. Do {toFull} more exercise{(toFull == 1 ? "" : "s")} for full points."
            : null;
        IsReturnOpen = true;
    }

    [RelayCommand]
    private async Task ContinueWorkoutAsync()
    {
        IsReturnOpen = false;
        try { await _watcher.ContinueAsync(); }
        catch (Exception) { /* the clock restarts with the next set anyway */ }
    }

    /// <summary>"Finish workout early": with half or more, the Finish sheet (cardio, then the result); below half the
    /// session ends without logging, as the popup said.</summary>
    [RelayCommand]
    private async Task FinishEarlyAsync()
    {
        IsReturnOpen = false;
        _watcher.ClearReturn();
        if (Live is not { } card) return;
        if (card.Info.Status.CanLog) card.OpenFinishCommand.Execute(null);
        else await card.EndCommand.ExecuteAsync(null);
    }

    private LiveWorkoutCardViewModel Card(int userId, LiveWorkoutInfo info) =>
        new(_liveWorkouts, _sets, _location, userId, info, changed =>
        {
            UpdateHero();
            _watcher.ShowActivity(changed);   // each set moves the island's percent
        }, LiveEndedAsync, () => _shell().ShowGyms(addGym: true));

    /// <summary>Phones: whether Start workout can start, and the result line after a workout. The same messages and
    /// buttons as the check-in when there's nothing to start.</summary>
    private async Task RefreshStartAsync(int userId)
    {
        if (!HasLiveWorkout || _today is not { } today) return;
        var c = await _workouts.GetCheckInAsync(userId);
        ShowPickSplit = !c.HasSplit;
        // A rest day needs nothing built: its workout is picked exercise by exercise
        ShowBuildWorkout = c.HasSplit && c.PlannedDay is not null && c.Exercises.Count == 0;
        CanStart = c.HasSplit && !ShowBuildWorkout && !HasMissedDay && !today.Logged;
        StartLabel = c.HasSplit && c.PlannedDay is null ? "Start workout anyway" : "Start workout";

        ResultText = null;
        if (today.Logged && !IsLive)
        {
            var lines = (await _points.GetAsync(userId))?.History
                .Where(l => l.Date == today.Date && l.Reason is PointsReason.Workout or PointsReason.Cardio).ToList() ?? [];
            var earned = lines.Sum(l => l.Amount);
            var record = new WorkoutRecord(today.Date, today.FilledExercises, today.TotalExercises, today.Verified);
            ResultText = today.Verified == false ? $"+{earned} pts · not verified"
                : PointsCalculator.IsComplete(record) || today is not { FilledExercises: { } f, TotalExercises: { } t }
                    ? $"+{earned} pts · full points"
                    : $"+{earned} pts · half points, {f} of {t} exercises";
        }
    }

    /// <summary>Start workout: today's planned day; on a rest day the user picks the exercises first.</summary>
    [RelayCommand]
    private Task StartWorkoutAsync() => StartAsync(null);

    private async Task StartAsync(IReadOnlyList<int>? exerciseIds)
    {
        if (_session.CurrentUser is not { } user) return;
        try
        {
            Error = null;
            var r = await _liveWorkouts.StartAsync(user.Id, exerciseIds: exerciseIds);
            if (r.NeedsDay)
            {
                var choices = await _workouts.GetPickerChoicesAsync(user.Id);
                Picker = new RestDayPickerViewModel(choices, StartAsync, () => Picker = null);
                return;
            }
            if (!r.Success || r.Workout is null)
            {
                if (Picker is not null) Picker.Error = r.Error; else Error = r.Error;
                return;
            }
            Picker = null;
            ShowEndedNote = false;
            StopNoteText = null;
            PointsNotice = null;
            PointsLostNotice = null;
            // The first Start asks to notify, for "Continue your workout?" (18b), then for location (18c), before the
            // card reads it. Refused: no nudge, and the workout runs unverified ("Location is off")
            if (await _liveWorkouts.FirstStartAsync(user.Id))
            {
                if (_notifications.IsSupported) await _notifications.RequestPermissionAsync();
                if (_location.IsSupported && !_location.HasPermission && (!_location.NeedsNoteBeforePrompt || await AskLocationNoteAsync()))
                    await _location.RequestPermissionAsync();
            }
            Live = Card(user.Id, r.Workout);
            UpdateHero();
        }
        catch (Exception)
        {
            Error = "Could not start the workout.";
        }
    }


    // ===== Android's location prompt has no word from the app, so a note comes first (18c) =====
    [ObservableProperty] public partial bool IsLocationNoteOpen { get; set; }
    private TaskCompletionSource<bool>? _locationNote;

    /// <summary>Shows the note; true for Continue (then the system asks), false for Not now.</summary>
    private Task<bool> AskLocationNoteAsync()
    {
        _locationNote?.TrySetResult(false);
        _locationNote = new TaskCompletionSource<bool>();
        IsLocationNoteOpen = true;
        return _locationNote.Task;
    }

    [RelayCommand] private void LocationNoteContinue() => AnswerLocationNote(true);
    [RelayCommand] private void LocationNoteNotNow() => AnswerLocationNote(false);

    private void AnswerLocationNote(bool allow)
    {
        IsLocationNoteOpen = false;
        _locationNote?.TrySetResult(allow);
    }

    /// <summary>Finish or End: the card goes, and Home shows the result (the first place it appears) or, ended below
    /// half, the note that nothing was logged.</summary>
    private async Task LiveEndedAsync(LiveEndResult result)
    {
        Live = null;
        if (_session.CurrentUser is not { } user) return;
        if (result.Log is { } log)
        {
            await CheckedInAsync(log);
            await RefreshStartAsync(user.Id);
            CardioPromptError = null;
            IsCardioPromptOpen = result.Success;   // "Continue with a cardio session?"
            return;
        }
        ShowEndedNote = result.Outcome == LiveOutcome.NotCounted;
        await RefreshAsync();
    }

    /// <summary>Today's workout, this week's row and recent PRs. They change with a check-in, an undo or a missed-day
    /// choice, so those refresh them too. A failure only empties the cards.</summary>
    private async Task RefreshDashboardAsync(int userId)
    {
        try
        {
            var today = AppClock.Today;
            TodayWorkout.Apply(await _workouts.GetTodayWorkoutAsync(userId, today));

            var chips = await _streaks.GetWeekAsync(userId, today);
            var names = new Dictionary<DateOnly, string>();
            foreach (var c in chips)
            {
                if (c.Kind == WeekChipKind.Rest) { names[c.Date] = "Rest"; continue; }
                var day = await _workouts.GetTodayAsync(userId, c.Date);
                if (day.HasSplit) names[c.Date] = day.IsTraining ? day.Title : "Rest";
            }
            Week.Apply(chips, names);

            RecentPrs.Apply(await _sets.GetRecentPrsAsync(userId));
            ShowSetsHint = HasCheckIn && _today is { Logged: false } && !HasMissedDay && await _sets.HasSetsTodayAsync(userId, today);
            GymsThisMonth = (await _gymSummary.GetMonthAsync(userId, today.Year, today.Month)).DistinctGyms
                .ToString(CultureInfo.InvariantCulture);
            UpdateHero();
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
        var today = AppClock.Today;
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
            _makeUpFor = await _streaks.MakeUpDayAsync(user.Id, AppClock.Today);
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

    /// <summary>"#2 of 5 this week" and the sidebar's inbox badge. Runs after the user's own sync, so their
    /// weekly points are already current. A failure only hides the card's details.</summary>
    private async Task RefreshFriendsAsync(int userId)
    {
        try
        {
            await _inbox.RefreshUnreadAsync(userId);

            var board = await _leaderboard.GetAsync(userId, IsAllTime ? LeaderboardPeriod.AllTime : LeaderboardPeriod.ThisWeek);
            HasFriends = board is { IsAlone: false };
            FriendsTitle = board is { IsAlone: false, You: { } you }
                ? $"#{you.Rank} of {board.Rows.Count} {(IsAllTime ? "all time" : "this week")}"
                : board is { IsStale: true } ? "Friends will show when you're back online."
                : "Add friends to compete.";
            FriendsStale = board is { IsStale: true, FetchedAt: not null } ? StaleText.For(board) : null;
            Board = board is { IsAlone: false }
                ? board.Rows.Take(BoardRows).Select(r => new BoardRowViewModel(r)).ToList()
                : [];
        }
        catch (Exception)
        {
            HasFriends = false;
            FriendsStale = null;
            FriendsTitle = "Could not load your friends.";
            Board = [];
        }
    }

    [RelayCommand]
    private async Task ShowBoardWeekAsync()
    {
        IsAllTime = false;
        if (_session.CurrentUser is { } user) await RefreshFriendsAsync(user.Id);
    }

    [RelayCommand]
    private async Task ShowBoardAllTimeAsync()
    {
        IsAllTime = true;
        if (_session.CurrentUser is { } user) await RefreshFriendsAsync(user.Id);
    }

    /// <summary>The four first-time steps: a split, exercises for every day of it (the split and today's exercises
    /// unlock check-in), body data and then a calorie goal, and a home city. Each one reads what the user already has,
    /// so the card fills itself in as they go.</summary>
    private async Task RefreshSetupAsync(int userId)
    {
        var checkIn = await _workouts.GetCheckInAsync(userId);
        var menu = checkIn.HasSplit ? await _menu.GetAsync(userId) : null;
        var calories = await _calories.GetAsync(userId);
        var profile = await _profiles.GetAsync(userId);
        var city = await _profiles.GetHomeCityAsync(userId);

        var splitResult = menu is null ? null
            : checkIn.PlannedDay is { } planned ? $"{menu.SplitName} · today: {planned.Name}" : $"{menu.SplitName} · rest today";
        var exercises = SetupPlan.Exercises(menu);
        var (body, opensProfile) = SetupPlan.Calories(calories?.Goal, profile);

        var steps = new List<SetupStepViewModel>
        {
            new(1, "Pick your split",
                "Choose a weekly plan, its variant and the day your first workout falls on. PPL and Upper / Lower are good starts.",
                "Choose a split", splitResult, isLocked: false, () => _shell().ShowSplits()),
            new(2, exercises.Title, exercises.Text, exercises.Cta, exercises.Result,
                isLocked: !checkIn.HasSplit, () => _shell().ShowWorkouts()),
            new(3, body.Title, body.Text, body.Cta, body.Result, isLocked: false,
                opensProfile ? () => _shell().ShowProfile(ProfilePage.BodyData) : () => _shell().ShowCalories()),
            new(4, "Choose your home city",
                "Loads the gyms near you, so you can log where you trained after checking in.",
                "Choose city", city?.Name, isLocked: false, () => _shell().ShowGyms(chooseCity: true)),
        };
        if (steps.FirstOrDefault(x => x.IsTodo) is { } next) next.IsNext = true;

        var done = steps.Count(x => x.IsDone);
        SetupSteps = steps;
        SetupDoneText = $"{done} / {steps.Count} done";
        SetupProgress = done / (double)steps.Count;
        ShowSetup = done < steps.Count;
    }

    private async Task RefreshCaloriesAsync(int userId)
    {
        var o = await _calories.GetAsync(userId);
        if (o is null) return;
        var g = o.Goal;

        var t = o.Today;
        var goal = t?.Goal ?? g.Goal;   // a logged day keeps the goal it was logged against
        CalorieGoalType = goal is null ? null : CalorieGoals.DisplayName(t?.GoalType ?? g.GoalType);

        IsCaloriesLogged = t is not null;
        CaloriesHit = t is { Hit: true };
        CaloriesCenter = t is null ? "—" : CalorieGoals.K(t.Consumed);
        CaloriesCenterSub = t is null ? "not logged" : "kcal eaten";
        CaloriesSweep = t is not null && goal is > 0 ? 360 * Math.Min(1, (double)t.Consumed / goal.Value) : 0;
        CaloriesTitle = t is null ? "Not logged yet" : CaloriesHit ? "On target" : "Logged";

        if (goal is { } gk)
        {
            var (lo, hi) = CalorieGoals.TargetRange(gk);
            CaloriesHint = $"Goal {CalorieGoals.K(gk)} kcal · on target between {CalorieGoals.K(lo)}–{CalorieGoals.K(hi)}";
            CaloriesTip = $"Log once, at the end of the day. Hitting the target gives +{PointsCalculator.CalorieTargetPoints} pts.";
            CaloriesResult = t is null ? ""
                : CaloriesHit ? $"On target · +{PointsCalculator.CalorieTargetPoints} pts"
                : t.Consumed < lo ? "Under the target range"
                : "Over the target range";
        }
        else
        {
            CaloriesHint = "No calorie goal yet. Set one on your profile.";
            CaloriesTip = null;
            CaloriesResult = t is null ? "" : $"{CalorieGoals.K(t.Consumed)} kcal logged";
        }
        GoalReviewHint = g.SuggestionChanged ? "Your suggested goal changed. Review it on your profile." : null;
        LogCaloriesLabel = t is null ? "Log" : "Update";
    }

    /// <summary>Phones: Add food opens the sheet for the meal that fits the time of day; the card reloads after each change.</summary>
    [RelayCommand]
    private void AddFood() => _shell().OpenAddFood(null, async result =>
    {
        if (_session.CurrentUser is not { } user) return;
        await RefreshCaloriesAsync(user.Id);
    });

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
            IsEditingCalories = false;
            ApplyPoints(r.Points);
            await RefreshCaloriesAsync(user.Id);

        }
        catch (Exception)
        {
            CaloriesError = "Could not save your calories.";
        }
    }

    [RelayCommand]
    private void EditCalories()
    {
        CaloriesError = null;
        CaloriesInput = CaloriesCenter.Replace(",", "");
        IsEditingCalories = true;
    }

    [RelayCommand]
    private void CancelEditCalories()
    {
        CaloriesError = null;
        CaloriesInput = "";
        IsEditingCalories = false;
    }

    private void Apply(TodayInfo today, StreakResult streak)
    {
        _today = today;
        _streak = streak;
        TodayTitle = !today.HasSplit ? "No split selected" : today.Title;
        TodayHint = !today.HasSplit ? "Pick a split to get a weekly schedule."
                  : today.Logged ? "Workout logged ✓"
                  : HasMissedDay ? WorkoutService.NeedsChoiceError
                  : today.IsTraining ? "Workout still needed today."
                  : "Rest day. You can still log an extra workout.";
        LogLabel = today.Logged ? "Logged ✓" : today is { HasSplit: true, IsTraining: false } ? "Log extra workout" : "I went today";
        CanLog = !today.Logged && !HasMissedDay;
        CanUndo = today.Logged && !IsLive && !IsCardio;   // today only: after midnight yesterday's log is read-only
        if (!today.Logged) IsConfirmingUndo = false;

        StreakText = $"{streak.Current} day streak · best {streak.Longest}";
        StreakNote = streak.Status switch
        {
            StreakStatus.NotStarted => "Log your first workout to start a streak.",
            StreakStatus.Active when streak.VacationUntil is { } back =>
                $"On vacation until {back.ToString("ddd d MMM", CultureInfo.InvariantCulture)}: your streak is frozen.",
            StreakStatus.Active when streak.Stale => "Rest day: your streak is on ice. It's kept, but only a workout adds to it.",
            StreakStatus.Active => "Streak alive, nice.",
            StreakStatus.PendingToday => "Log today's workout to keep your streak.",
            StreakStatus.NeedsChoice => "You missed yesterday. Choose what to do to keep your streak.",
            StreakStatus.AtRisk => today.HasSplit ? $"Do {today.Title} today to keep your streak."
                                                  : "Work out today to keep your streak.",
            StreakStatus.Shifted => "You're one day behind, so train tomorrow (or on your next rest day)",
            StreakStatus.Broken => "Streak broken. Log today's workout to start a new one.",
            _ => ""
        };

        StreakCurrent = streak.Current;
        BestStreakText = streak.Longest == 1 ? "1 day" : $"{streak.Longest} days";
        if (today.Logged || HasLiveWorkout) ShowSetsHint = false;
        UpdateHero();

        // keep the in-memory session user in sync
        if (_session.CurrentUser is { } u)
        {
            u.CurrentStreak = streak.Current;
            u.LongestStreak = Math.Max(u.LongestStreak, streak.Longest);
        }
    }

    /// <summary>The gauge and the line under it, from today's plan, the streak and the make-up day.</summary>
    private void UpdateHero()
    {
        if (_today is not { } today || _streak is not { } streak) return;
        var rest = today is { HasSplit: true, IsTraining: false };
        var live = Live;
        var logged = today.Logged && live is null;   // logged silently during a live workout: not done yet on screen
        IsDone = logged;
        IsRestDay = rest;
        IsTrainingOpen = !logged && !rest && live is null;
        IsRestOpen = !logged && rest && live is null;

        // Phones: the arc shows the share of exercises done, live or after the workout (a log from before 0.18.0 is whole)
        var share = live is not null ? live.Progress
            : logged ? (today is { FilledExercises: { } f, TotalExercises: > 0 and var t } ? Math.Min(1, (double)f / t) : 1)
            : 0;
        ShowProgressArc = HasLiveWorkout && (live is not null || logged);
        ProgressSweep = 180 * share;
        IsStreakStale = streak.Stale && live is null;
        IsArcHot = ShowProgressArc && share > 0 && !IsStreakStale;
        ShowRestArc = IsRestOpen && !IsStreakStale;
        ShowColdArc = !IsRestOpen && !IsArcHot && !IsStreakStale;
        ShowFireArc = HasCheckIn && logged && !IsStreakStale;
        IsTitleHot = !IsStreakStale && (HasLiveWorkout ? IsArcHot : logged);
        ShowStartRow = HasLiveWorkout && live is null && !logged;
        ShowDoneRow = HasLiveWorkout && live is null && logged;
        if (live is not null)
        {
            IsMakeUpDay = false;
            IsOnVacation = false;
            ShowImBack = false;
            ShowNearbyGyms = false;
            GaugeKicker = "WORKOUT IN PROGRESS";
            GaugeTitle = live.Info.DayName.ToUpperInvariant();
            GaugeSub = live.ProgressText;
            return;
        }

        IsMakeUpDay = _makeUpFor is not null;
        GaugeKicker = _makeUpFor is { } owed
            ? $"MAKE-UP · FOR {owed.ToString("dddd", CultureInfo.InvariantCulture).ToUpperInvariant()}"
            : "TODAY";
        GaugeTitle = !today.HasSplit ? "WORKOUT"
                   : rest && !today.Logged ? "REST"
                   : rest ? "EXTRA"
                   : today.Title.ToUpperInvariant();

        var count = TodayWorkout.Exercises.Count;
        GaugeSub = !today.HasSplit ? "No split selected"
                 : HasLiveWorkout && today is { Logged: true, FilledExercises: { } done, TotalExercises: { } all }
                     ? $"{done} of {all} exercise{(all == 1 ? "" : "s")}"
                 : rest ? (today.Logged ? (streak.Stale ? "Extra workout · streak on ice" : "Extra workout logged")
                           : !streak.Stale ? "Enjoy the rest."
                           : HasLiveWorkout ? "On ice. A gym workout adds a day."
                           : "Rest up. Your streak is on ice.")
                 : today.Logged ? "Done for today"
                 : count > 0 ? $"{count} exercise{(count == 1 ? "" : "s")} planned"
                 : "No exercises built yet";

        StatusText = ShowEndedNote && !today.Logged ? "Start again when you're back at the gym."
            : today.Logged
            ? (rest && streak.Stale ? "Extra workout logged. Rest days keep your streak without adding to it."
               : rest ? $"Extra workout logged. Streak is now {streak.Current} day{(streak.Current == 1 ? "" : "s")}."
               : $"{Capitalize(today.Title)} done. Streak is now {streak.Current} day{(streak.Current == 1 ? "" : "s")}.")
            : !today.HasSplit ? "Pick a split to get a weekly schedule."
            : rest ? "Rest day. Feel like training anyway?"
            : _makeUpFor is { } d ? $"Train today to make up {d.ToString("dddd", CultureInfo.InvariantCulture)} and keep your streak."
            : streak.Status is StreakStatus.Broken or StreakStatus.NotStarted || streak.Current == 0
                ? "Today starts a new streak."
                : $"Not done yet. Go to reach {streak.Current + 1} days.";

        ApplyVacation(today, streak, logged);
    }

    /// <summary>A vacation day (0.19.0, the GainStreak Mobile canvas's Home on vacation): the card says FROZEN and when
    /// the user is back. A verified workout still adds a day, so after one the card reads as usual.</summary>
    private void ApplyVacation(TodayInfo today, StreakResult streak, bool logged)
    {
        ShowImBack = Vacation.CanEnd && Live is null;
        IsOnVacation = streak.VacationUntil is not null && Live is null;
        ShowNearbyGyms = IsOnVacation && _location.IsSupported;
        if (!IsOnVacation || streak.VacationUntil is not { } back) return;
        if (logged && !streak.Stale) return;                                   // a verified workout: it added a day

        IsMakeUpDay = false;
        GaugeKicker = "ON VACATION";
        GaugeTitle = "FROZEN";
        GaugeSub = "Back on " + back.ToString("dddd, d MMM", CultureInfo.InvariantCulture);
        StatusText = logged
            ? "Workout logged. On vacation, only a workout at a gym adds a day."
            : HasLiveWorkout ? "Your streak is frozen until you're back. A workout at a gym still adds a day."
            : "Your streak is frozen until you're back.";
    }

    private static string Capitalize(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

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
            // "Ask for gym" off: your gym was logged by itself, so the sharing question comes now
            ShowShareQuestion = gym is { Gym: not null, ShareGymCheckIns: null };
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
        UndoLossText = $"−{PointsCalculator.WorkoutPoints} Workout";
        if (_session.CurrentUser is { } user)
        {
            try
            {
                var today = AppClock.Today;
                var lines = (await _points.GetAsync(user.Id))?.History.Where(l => l.Date == today).ToList() ?? [];
                if (lines.Count > 0)
                {
                    // Calories stay logged, so their points aren't lost with the check-in
                    var groups = lines.Where(l => l.Reason != PointsReason.CalorieTarget).GroupBy(l => l.Reason).ToList();
                    if (groups.Count > 0)
                    {
                        earned = "today's " + string.Join(" and ", groups
                            .Select(g => $"+{N(g.Sum(l => l.Amount))} {PointsCalculator.DisplayName(g.Key)}"));
                        UndoLossText = string.Join(" · ", groups
                            .Select(g => $"−{N(g.Sum(l => l.Amount))} {PointsCalculator.DisplayName(g.Key)}"));
                    }
                }
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
            TodayGymName = info.Gym?.Name;
            ShowMakeMyGym = info.HomeGym is null && info.Suggested is not null;
            _shareGymCheckIns = info.ShareGymCheckIns;
            ShowAddGym = info is { LoggedToday: true, Gym: null };
            _lastGym = info.Suggested;   // your gym, else the last one used
            HasLastGym = info.Suggested is not null;
            LastGymLabel = info.Suggested is { } last ? last.Name : "";
            return info;
        }
        catch (Exception)
        {
            ShowGymLink = false;
            ShowAddGym = false;
            TodayGymName = null;
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
            if (ShowMakeMyGym && MakeMyGym) await _profiles.SetHomeGymAsync(user.Id, gym.Id);
            if (DontAskForGym) await NeverAskForGymAsync();
            await RefreshGymAsync(user.Id);
            ShowShareQuestion = _shareGymCheckIns is null;
            await RefreshDashboardAsync(user.Id);   // gyms this month

        }
        catch (Exception)
        {
            Error = "Could not save the gym.";
        }
    }

    [RelayCommand]
    private void ChooseGym() => _shell().ShowGyms(pickToday: true);

    /// <summary>The one-time sharing question: Share or Not now. Either way it's never asked again.</summary>
    [RelayCommand] private Task ShareCheckInsAsync() => AnswerShareAsync(true);
    [RelayCommand] private Task DontShareCheckInsAsync() => AnswerShareAsync(false);

    private async Task AnswerShareAsync(bool share)
    {
        try
        {
            if (_session.CurrentUser is not { } user) return;
            ShowShareQuestion = false;
            var r = await _profiles.SetShareGymCheckInsAsync(user.Id, share);
            if (!r.Success) { Error = r.Error; return; }
            _shareGymCheckIns = share;
            GymNotice = share ? "Friends now see the gym and day of your check-ins. Change it on the Map."
                              : "Okay, your check-ins stay private. You can change this on the Map.";
        }
        catch (Exception)
        {
            Error = "Could not save that setting.";
        }
    }

    // Skip only hides it: the prompt appears right after "I went today", which can't happen twice in a day
    [RelayCommand]
    private async Task SkipGymPromptAsync()
    {
        ShowGymPrompt = false;
        if (DontAskForGym) await NeverAskForGymAsync();
    }

    /// <summary>"Log gym" in the prompt: the last gym when there is one, otherwise the picker.</summary>
    [RelayCommand]
    private async Task LogGymAsync()
    {
        if (HasLastGym) { await UseLastGymAsync(); return; }
        if (DontAskForGym) await NeverAskForGymAsync();
        ChooseGym();
    }

    [RelayCommand]
    private async Task NeverAskForGymAsync()
    {
        try
        {
            if (_session.CurrentUser is not { } user) return;
            ShowGymPrompt = false;
            var r = await _profiles.SetAskForGymAfterCheckInAsync(user.Id, false);
            if (!r.Success) { Error = r.Error; return; }
            GymNotice = "Okay, no more gym questions. Your gym is logged by itself; change it on the Map.";

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

        LevelNumber = level.Number;
        LevelTitle = level.Name;
        PointsText = $"{N(sync.NewTotal)} pts";
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

    // ===== Points history popup =====
    public PointsHistoryViewModel PointsHistory { get; }
    [ObservableProperty] public partial bool IsPointsHistoryOpen { get; set; }

    [RelayCommand]
    private void ShowPointsHistory()
    {
        _ = PointsHistory.Open(() => IsPointsHistoryOpen = false);
        IsPointsHistoryOpen = true;
    }
    [RelayCommand]
    private Task Logout() => _accounts.LogOutAsync();
    [RelayCommand]
    private void ShowSplits() => _shell().ShowSplits();
    [RelayCommand]
    private void ShowWorkouts() => _shell().ShowWorkouts();
    [RelayCommand]
    private void ShowProfile() => _shell().ShowProfile();
    [RelayCommand]
    private void ShowCalories() => _shell().ShowCalories();
    [RelayCommand]
    private void ShowGymSummary() => _shell().ShowGyms();
    [RelayCommand]
    private void ShowFriends() => _shell().ShowFriends();
    [RelayCommand]
    private void ShowLeaderboard() => _shell().ShowFriends();   // the leaderboard is on the friends screen
}
