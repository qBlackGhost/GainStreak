using System;
using System.Collections.Generic;
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
    [NotifyPropertyChangedFor(nameof(HasUpdateNotice))]
    public partial string? UpdateNotice { get; set; }

    public bool HasUpdateNotice => UpdateNotice is not null;

    [RelayCommand] private void DismissUpdateNotice() => UpdateNotice = null;

    /// <summary>Restarts into an update that has finished downloading (see AppUpdater), or null when none is waiting.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRestartForUpdate))]
    public partial Action? RestartForUpdate { get; set; }

    public bool CanRestartForUpdate => RestartForUpdate is not null;

    [RelayCommand] private void RestartNow() => RestartForUpdate?.Invoke();

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

    // ===== Navigation (16d) =====
    // Tab roots (Home, Workouts, Calories, Friends, More) clear the back stack; every other screen is pushed on it.
    // Opening a screen already in the stack goes back to it instead, so a screen's own back button (Day's "Back"
    // calls ShowWorkouts, Exercise's ShowDay) never leaves a loop behind. Going back opens the screen again rather
    // than reusing it, so it shows what changed in the meantime, as those buttons always have. The desktop shows
    // none of this; the phones show the tab bar and a back button (MainView).

    /// <summary>A screen in the back stack: its type, and how to open it again.</summary>
    private sealed record Entry(Type Type, Func<ViewModelBase> Open);

    private readonly List<Entry> _back = [];
    private Entry? _current;

    /// <summary>The tab of the screen at the bottom of the stack (a screen pushed from Home stays under Home), or null
    /// on the screens before sign-in is done (login, register, profile setup).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsTabBar))]
    public partial AppTab? SelectedTab { get; private set; }

    /// <summary>Whether there's a screen to go back to.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsTopBar))]
    public partial bool CanGoBack { get; private set; }

    /// <summary>Under 700 px wide (MainView sets it): the tab bar and top bar instead of the sidebar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsTabBar), nameof(ShowsTopBar))]
    public partial bool IsPhone { get; set; }

    /// <summary>The tab bar, on phones, on every screen after sign-in.</summary>
    public bool ShowsTabBar => IsPhone && SelectedTab is not null;

    /// <summary>The top bar with the back button and the title, on phones, on a pushed screen.</summary>
    public bool ShowsTopBar => IsPhone && CanGoBack;

    /// <summary>The top bar's title on a pushed screen.</summary>
    [ObservableProperty] public partial string Title { get; private set; } = "";

    /// <summary>The tab a screen belongs to when it's the root of the stack.</summary>
    public static AppTab? TabOf(Type screen) => screen.Name switch
    {
        nameof(LoginViewModel) or nameof(RegisterViewModel) or nameof(ProfileSetupViewModel) => null,
        nameof(HomeViewModel) => AppTab.Home,
        nameof(WorkoutsViewModel) or nameof(DayViewModel) or nameof(ExerciseViewModel)
            or nameof(CustomExercisesViewModel) => AppTab.Workouts,
        nameof(CaloriesViewModel) => AppTab.Calories,
        nameof(FriendsViewModel) => AppTab.Friends,
        _ => AppTab.More,   // More and what it lists: Profile, Splits (and its screens), Map, Inbox
    };

    private static string TitleOf(Type screen) => screen.Name switch
    {
        nameof(DayViewModel) => "Workout",
        nameof(ExerciseViewModel) => "Exercise",
        nameof(CustomExercisesViewModel) => "Custom exercises",
        nameof(SplitsViewModel) => "Splits",
        nameof(SplitDetailViewModel) => "Split",
        nameof(SplitEditorViewModel) => "Edit split",
        nameof(GymsViewModel) => "Map",
        nameof(InboxViewModel) => "Inbox",
        nameof(ProfileViewModel) => "Profile",
        nameof(WorkoutsViewModel) => "Workouts",
        nameof(CaloriesViewModel) => "Calories",
        nameof(FriendsViewModel) => "Friends",
        _ => "",
    };

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
        SelectedTab = TabOf(_back.Count > 0 ? _back[0].Type : entry.Type);
        CanGoBack = _back.Count > 0;
        Title = TitleOf(entry.Type);
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

    [RelayCommand] private void Back() => GoBack();

    public void ShowRegister() => Open(_sp.GetRequiredService<RegisterViewModel>, root: true);
    public void ShowLogin() => Open(_sp.GetRequiredService<LoginViewModel>, root: true);
    public void ShowHome() => Open(_sp.GetRequiredService<HomeViewModel>, root: true);
    public void ShowWorkouts() => Open(_sp.GetRequiredService<WorkoutsViewModel>, root: true);
    public void ShowCalories() => Open(_sp.GetRequiredService<CaloriesViewModel>, root: true);
    /// <summary>Friends, with the leaderboard on the same screen.</summary>
    public void ShowFriends() => Open(_sp.GetRequiredService<FriendsViewModel>, root: true);
    /// <summary>The phones' More tab: profile, splits, map, inbox, feedback.</summary>
    public void ShowMore() => Open(_sp.GetRequiredService<MoreViewModel>, root: true);

    public void ShowSplits() => Open(_sp.GetRequiredService<SplitsViewModel>, root: false);
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

/// <summary>The phones' tabs (TabBar), in their order.</summary>
public enum AppTab { Home, Workouts, Calories, Friends, More }

/// <summary>A screen with popups of its own (a confirmation, a picker, a sheet) that back should close first.</summary>
public interface IClosesOnBack
{
    /// <summary>Closes the topmost open popup and returns true, or returns false when none is open.</summary>
    bool CloseOnBack();
}
