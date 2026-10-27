using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GymApp.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Contracts;
using GymApp.Services;
using Microsoft.Extensions.DependencyInjection;
using GymApp.Data.Models;

namespace GymApp.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly SessionService _session;
    private readonly IServiceProvider _sp;

    [ObservableProperty]
    public partial ViewModelBase Current { get; set; } = null!;

    /// <summary>The "Something went wrong" banner from the global error handler, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGlobalError))]
    public partial string? GlobalError { get; set; }

    public bool HasGlobalError => GlobalError is not null;

    [RelayCommand] private void DismissGlobalError() => GlobalError = null;

    /// <summary>The server's "please update" for a build it no longer accepts, or that an update is ready, or null.
    /// Workouts keep working; the server's comes back with the next refused call if dismissed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdateNotice), nameof(CanOpenStore))]
    public partial string? UpdateNotice { get; set; }

    public bool HasUpdateNotice => UpdateNotice is not null;

    [RelayCommand] private void DismissUpdateNotice() => UpdateNotice = null;

    /// <summary>Restarts into an update that has finished downloading (see AppUpdater), or null when none is waiting.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRestartForUpdate), nameof(CanOpenStore))]
    public partial Action? RestartForUpdate { get; set; }

    public bool CanRestartForUpdate => RestartForUpdate is not null;

    [RelayCommand] private void RestartNow() => RestartForUpdate?.Invoke();

    /// <summary>The phones' "Update" on the server's "please update" (16f): the store installs updates there. Not while
    /// an update is waiting to restart into (the desktop's Velopack).</summary>
    public bool CanOpenStore => HasUpdateNotice && !CanRestartForUpdate && _sp.GetRequiredService<IPlatform>().CanOpenStorePage;

    [RelayCommand] private void OpenStore() => _sp.GetRequiredService<IPlatform>().OpenStorePage();

    /// <summary>The feedback popup, over whatever screen is showing (the sidebar's Support section opens it).</summary>
    public FeedbackViewModel Feedback => _sp.GetRequiredService<FeedbackViewModel>();
    [ObservableProperty] public partial bool IsFeedbackOpen { get; set; }

    public void ShowFeedback(FeedbackKind kind = FeedbackKind.Problem)
    {
        Feedback.Open(() => IsFeedbackOpen = false, kind);
        IsFeedbackOpen = true;
    }

    public MainViewModel(SessionService session, IServiceProvider sp)
    {
        _session = session;
        _sp = sp;
        _session.Changed += Refresh;
        Refresh();
    }

    private void Refresh()
    {
        IsFeedbackOpen = false;
        // A brand-new account gets the profile setup first; it can be skipped to Home
        if (!_session.IsLoggedIn) ShowLogin();
        else if (_session.IsNewAccount) Open(_sp.GetRequiredService<ProfileSetupViewModel>, root: true);
        else ShowHome();
    }

    // ===== Navigation (16d, after the GainStreak Mobile canvas in 16e) =====
    // The tabs (Splits, Workouts, Home, Calories, Friends) clear the back stack; every other screen is pushed on it.
    // Opening a screen already in the stack goes back to it instead, so a screen's own back button (Day's "Workouts"
    // calls ShowWorkouts, Exercise's ShowDay) never leaves a loop behind. Going back opens the screen again rather
    // than reusing it, so it shows what changed in the meantime, as those buttons always have. The desktop shows
    // none of this; the phones show the app bar and the tab bar (MainView), and the system back button uses it.

    /// <summary>A screen in the back stack: its type, and how to open it again.</summary>
    private sealed record Entry(Type Type, Func<ViewModelBase> Open);

    private readonly List<Entry> _back = [];
    private Entry? _current;

    /// <summary>The lit tab: the one the current screen belongs to, or null on a screen of no tab (Profile, Inbox)
    /// and on the screens before sign-in is done.</summary>
    [ObservableProperty] public partial AppTab? SelectedTab { get; private set; }

    /// <summary>Whether there's a screen to go back to.</summary>
    [ObservableProperty] public partial bool CanGoBack { get; private set; }

    /// <summary>A screen after sign-in (not login, register or the profile setup): it gets the app bar and tabs.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsBars))]
    public partial bool IsSignedInScreen { get; private set; }

    /// <summary>Under 700 px wide (MainView sets it): the app bar and the tab bar instead of the sidebar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsBars))]
    public partial bool IsPhone { get; set; }

    /// <summary>The app bar and the tab bar, on phones, on every screen after sign-in.</summary>
    public bool ShowsBars => IsPhone && IsSignedInScreen;

    /// <summary>The app bar's streak, refreshed whenever the screen changes.</summary>
    [ObservableProperty] public partial int Streak { get; private set; }

    /// <summary>The app bar's avatar.</summary>
    public string Initials => _session.CurrentUser?.Username is { Length: > 0 } name
        ? name[..Math.Min(2, name.Length)].ToUpperInvariant() : "?";

    /// <summary>The tab a screen belongs to: null for the screens before sign-in and those the app bar opens.</summary>
    public static AppTab? TabOf(Type screen) => screen.Name switch
    {
        nameof(SplitsViewModel) or nameof(SplitDetailViewModel) or nameof(SplitEditorViewModel) => AppTab.Splits,
        nameof(WorkoutsViewModel) or nameof(DayViewModel) or nameof(ExerciseViewModel)
            or nameof(CustomExercisesViewModel) => AppTab.Workouts,
        nameof(HomeViewModel) => AppTab.Home,
        nameof(CaloriesViewModel) => AppTab.Calories,
        nameof(FriendsViewModel) or nameof(GymsViewModel) => AppTab.Friends,   // the Leaderboard / Map toggle
        _ => null,   // Login, Register, ProfileSetup; Profile and Inbox (the app bar)
    };

    private static bool IsBeforeSignIn(Type screen) =>
        screen == typeof(LoginViewModel) || screen == typeof(RegisterViewModel) || screen == typeof(ProfileSetupViewModel);

    /// <summary>Shows a screen. <paramref name="open"/> creates and loads it; it runs again when going back to it.</summary>
    private void Open(Func<ViewModelBase> open, bool root)
    {
        // The feedback popup covers the screen but not the phones' tab bar: a tab, or a screen opened any other way,
        // closes it. The note survives (FeedbackViewModel is a singleton)
        IsFeedbackOpen = false;
        var vm = open();
        var type = vm.GetType();
        if (root) _back.Clear();
        else
        {
            var i = _back.FindIndex(e => e.Type == type);
            if (i >= 0) _back.RemoveRange(i, _back.Count - i);
            else if (_current is not null && _current.Type != type) _back.Add(_current);
        }
        Show(new Entry(type, open), vm);
    }

    private void Show(Entry entry, ViewModelBase vm)
    {
        _current = entry;
        Current = vm;
        SelectedTab = TabOf(entry.Type);
        CanGoBack = _back.Count > 0;
        IsSignedInScreen = !IsBeforeSignIn(entry.Type);
        OnPropertyChanged(nameof(Initials));
        if (IsSignedInScreen) _ = RefreshStreakAsync();
        _sp.GetService<ReminderSync>()?.RequestRefresh();   // a screen change may follow a choice or a new split (16g)
    }

    private async Task RefreshStreakAsync()
    {
        if (_session.CurrentUser is not { } user) return;
        try { Streak = (await _sp.GetRequiredService<StreakService>().GetAsync(user.Id, AppClock.Today)).Current; }
        catch (Exception e) { _sp.GetRequiredService<ErrorLog>().Write(e, "App bar streak"); }
    }

    /// <summary>Back (the phones' back button, Android's system back): closes an open popup first, then goes to the
    /// previous screen. False at a tab root with nothing open, so Android leaves the app.</summary>
    public bool GoBack()
    {
        if (IsFeedbackOpen) { Feedback.CloseCommand.Execute(null); return true; }
        if (Current is IClosesOnBack screen && screen.CloseOnBack()) return true;
        if (_back.Count == 0) return false;
        var previous = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        Show(previous, previous.Open());
        return true;
    }


    public void ShowRegister() => Open(_sp.GetRequiredService<RegisterViewModel>, root: true);
    public void ShowLogin() => Open(_sp.GetRequiredService<LoginViewModel>, root: true);
    public void ShowHome() => Open(_sp.GetRequiredService<HomeViewModel>, root: true);
    public void ShowWorkouts() => Open(_sp.GetRequiredService<WorkoutsViewModel>, root: true);
    public void ShowCalories() => Open(_sp.GetRequiredService<CaloriesViewModel>, root: true);
    /// <summary>Friends, with the leaderboard on the same screen.</summary>
    public void ShowFriends() => Open(_sp.GetRequiredService<FriendsViewModel>, root: true);
    public void ShowSplits() => Open(_sp.GetRequiredService<SplitsViewModel>, root: true);

    public void ShowProfile() => Open(_sp.GetRequiredService<ProfileViewModel>, root: false);
    /// <summary>The Map screen: friends map, your gym, your check-ins. <paramref name="pickToday"/> opens the gym
    /// picker for today right away (Home's "Choose another…" and "Change gym").</summary>
    public void ShowGyms(bool pickToday = false, bool chooseCity = false)
    {
        var first = true;
        Open(() =>
        {
            var vm = _sp.GetRequiredService<GymsViewModel>();
            // Only the first time; going back to the map doesn't open the picker again
            if (first && pickToday) vm.PickTodayWhenLoaded();
            if (first && chooseCity) vm.ChooseCityWhenLoaded();
            first = false;
            return vm;
        }, root: false);
    }

    /// <summary>Friend requests now; workouts friends share, later.</summary>
    public void ShowInbox() => Open(() =>
    {
        var vm = _sp.GetRequiredService<InboxViewModel>();
        _ = vm.LoadAsync();
        return vm;
    }, root: false);

    public void ShowDay(MenuDay day, string splitName) => Open(() =>
    {
        var vm = _sp.GetRequiredService<DayViewModel>();
        vm.Load(day, splitName);
        return vm;
    }, root: false);

    public void ShowExercise(MenuDay day, string splitName, int exerciseId) => Open(() =>
    {
        var vm = _sp.GetRequiredService<ExerciseViewModel>();
        vm.Load(day, splitName, exerciseId);
        return vm;
    }, root: false);

    public void ShowCustomExercises(MenuDay day, string splitName) => Open(() =>
    {
        var vm = _sp.GetRequiredService<CustomExercisesViewModel>();
        vm.Load(day, splitName);
        return vm;
    }, root: false);

    public void ShowSplitEditor(WorkoutSplit? split) => Open(() =>
    {
        var vm = _sp.GetRequiredService<SplitEditorViewModel>();
        vm.Load(split);      // null = new split, otherwise pre-fills the form
        return vm;
    }, root: false);

    public void ShowSplitDetail(WorkoutSplit split) => Open(() =>
    {
        var vm = _sp.GetRequiredService<SplitDetailViewModel>();
        vm.Load(split);
        return vm;
    }, root: false);
}

/// <summary>The phones' tabs (TabBar), in their order; Home is the raised one in the middle.</summary>
public enum AppTab { Splits, Workouts, Home, Calories, Friends }

/// <summary>A screen with popups of its own (a confirmation, a picker, a sheet) that back should close first.</summary>
public interface IClosesOnBack
{
    /// <summary>Closes the topmost open popup and returns true, or returns false when none is open.</summary>
    bool CloseOnBack();
}
