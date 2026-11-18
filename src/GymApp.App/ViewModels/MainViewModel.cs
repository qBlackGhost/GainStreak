using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>The popup with a form of its own (a new split, a new exercise), over the current screen, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPopupOpen))]
    public partial ViewModelBase? Popup { get; private set; }

    public bool IsPopupOpen => Popup is not null;

    public void ClosePopup() => Popup = null;

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
        else if (_session.IsNewAccount && _sp.GetRequiredService<IAccountService>().EmailConfirmed == false) ShowConfirmEmail(afterSignUp: true, sendCode: true);
        else if (_session.IsNewAccount) ShowProfileSetup();
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
    /// <summary>A rest day that didn't add to the streak: the app bar's flame goes cold (ice).</summary>
    [ObservableProperty] public partial bool IsStreakStale { get; private set; }

    /// <summary>The user's own avatar, for the app bar and sidebar; the level is for its ring.</summary>
    [ObservableProperty] public partial string? OwnAvatar { get; private set; }
    [ObservableProperty] public partial int OwnLevel { get; private set; }

    /// <summary>The app bar's avatar.</summary>
    public string Initials => UserInitials.Of(_session.CurrentUser?.Username);

    /// <summary>The tab a screen belongs to: null for the screens before sign-in and those the app bar opens.</summary>
    public static AppTab? TabOf(Type screen) => screen.Name switch
    {
        nameof(SplitsViewModel) or nameof(SplitDetailViewModel) => AppTab.Splits,
        nameof(WorkoutsViewModel) or nameof(DayViewModel) or nameof(ExerciseViewModel)
            or nameof(CustomExercisesViewModel) => AppTab.Workouts,
        nameof(HomeViewModel) => AppTab.Home,
        nameof(CaloriesViewModel) => AppTab.Calories,
        nameof(FriendsViewModel) or nameof(GymsViewModel) => AppTab.Friends,   // the Leaderboard / Map toggle
        _ => null,   // Login, Register, ProfileSetup; Profile and Inbox (the app bar)
    };

    private static bool IsBeforeSignIn(Type screen) =>
        screen == typeof(LoginViewModel) || screen == typeof(RegisterViewModel) || screen == typeof(ProfileSetupViewModel)
        || screen == typeof(ConfirmEmailViewModel) || screen == typeof(ForgotPasswordViewModel);

    /// <summary>Shows a screen. <paramref name="open"/> creates and loads it; it runs again when going back to it.</summary>
    private void Open(Func<ViewModelBase> open, bool root)
    {
        // The feedback popup covers the screen but not the phones' tab bar: a tab, or a screen opened any other way,
        // closes it. The note survives (FeedbackViewModel is a singleton)
        IsFeedbackOpen = false;
        Popup = null;
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

    // ===== Links (Phase 21): a scanned QR code or a tapped invite link opens Friends with the confirm card =====

    private string? _pendingInviteCode;
    private bool _friendsAfterSignIn;

    /// <summary>An invite code waiting for Friends' confirm card, from a link the phone opened.</summary>
    public bool HasPendingInvite => _pendingInviteCode is not null;

    /// <summary>A link the phone opened (the heads call it on the UI thread, at a cold start too). An invite link
    /// opens Friends; before sign-in is done, the code waits in memory and Friends opens once it is. Other links are
    /// ignored. True when it was an invite link.</summary>
    public bool OpenLink(Uri link)
    {
        if (WidgetLink.TryParse(link) is { } target)
        {
            // A tapped widget or control (Phase 24). Before sign-in is done it waits for the first signed-in screen
            if (IsSignedInScreen) _ = OpenWidgetAsync(target);
            else _widgetAfterSignIn = target;
            return true;
        }
        if (GymApp.Social.InviteLink.TryParse(link) is not { } code) return false;
        _pendingInviteCode = code;
        if (IsSignedInScreen) ShowFriends();
        else _friendsAfterSignIn = true;
        return true;
    }

    private WidgetTarget? _widgetAfterSignIn;

    /// <summary>Opens what a tapped widget or control points at (Phase 24). On the UI thread.</summary>
    private async Task OpenWidgetAsync(WidgetTarget target)
    {
        switch (target)
        {
            case WidgetTarget.Home:
                ShowHome();
                break;
            case WidgetTarget.Points:
                ShowHome();
                (Current as HomeViewModel)?.ShowPointsHistoryCommand.Execute(null);
                break;
            case WidgetTarget.Body:
                ShowProfile(ProfilePage.BodyData);
                break;
            case WidgetTarget.StartWorkout:
                ShowHome();
                if (Current is HomeViewModel home) await home.StartWhenLoadedAsync();
                break;
            case WidgetTarget.Plan:
                // Today's day of the split; a rest day, or no split, has none: Splits
                MenuDay? day = null;
                string? split = null;
                if (_session.CurrentUser is { } user)
                {
                    try
                    {
                        if (await _sp.GetRequiredService<WorkoutMenuService>().GetAsync(user.Id, AppClock.Today) is { } menu)
                        {
                            day = menu.IsRestToday ? null : menu.Days.FirstOrDefault(d => d.IsToday);
                            split = menu.SplitName;
                        }
                    }
                    catch (Exception e) { _sp.GetRequiredService<ErrorLog>().Write(e, "Widget link"); }
                }
                if (day is not null) ShowDay(day, split!);
                else ShowSplits();
                break;
        }
    }

    /// <summary>The waiting code, once: <see cref="ShowFriends"/> hands it to the new Friends' confirm card, so leaving
    /// Friends drops the card.</summary>
    public string? TakePendingInviteCode()
    {
        var code = _pendingInviteCode;
        _pendingInviteCode = null;
        return code;
    }

    private void Show(Entry entry, ViewModelBase vm)
    {
        // The first screen after sign-in (or after the start, signed in) gives way to Friends when a link is waiting
        if (_friendsAfterSignIn && !IsBeforeSignIn(entry.Type) && entry.Type != typeof(FriendsViewModel))
        {
            _friendsAfterSignIn = false;
            ShowFriends();
            return;
        }
        if (entry.Type == typeof(FriendsViewModel)) _friendsAfterSignIn = false;

        _current = entry;
        Current = vm;
        SelectedTab = TabOf(entry.Type);
        CanGoBack = _back.Count > 0;
        IsSignedInScreen = !IsBeforeSignIn(entry.Type);
        OnPropertyChanged(nameof(Initials));
        if (IsSignedInScreen) _ = RefreshStreakAsync();
        _sp.GetService<ReminderSync>()?.RequestRefresh();   // a screen change may follow a choice or a new split (16g)
        _sp.GetService<WidgetSync>()?.RequestRefresh();   // and the Home Screen widgets show it (Phase 24)

        // A widget tapped before sign-in was done opens now, over the first signed-in screen
        if (IsSignedInScreen && _widgetAfterSignIn is { } target)
        {
            _widgetAfterSignIn = null;
            _ = OpenWidgetAsync(target);
        }
    }

    /// <summary>The app bar's streak, again: after a screen change, or a vacation starting or ending (0.19.0).</summary>
    public async Task RefreshStreakAsync()
    {
        if (_session.CurrentUser is not { } user) return;
        // During a live workout today's log may already exist (it's written silently at half): the bar shows the streak
        // without it until the workout ends
        try
        {
            var live = await _sp.GetRequiredService<LiveWorkoutService>().IsOpenAsync(user.Id);
            var streak = await _sp.GetRequiredService<StreakService>().GetAsync(user.Id, AppClock.Today, beforeTodaysLog: live);
            Streak = streak.Current;
            IsStreakStale = streak.Stale;
        }
        catch (Exception e) { _sp.GetRequiredService<ErrorLog>().Write(e, "App bar streak"); }
        await RefreshAvatarAsync();
    }

    /// <summary>Reads the user's avatar again, after the picker or with the screen.</summary>
    public async Task RefreshAvatarAsync()
    {
        if (_session.CurrentUser is not { } user) return;
        try
        {
            OwnAvatar = await _sp.GetRequiredService<ProfileService>().GetAvatarAsync(user.Id);
            OwnLevel = LevelCalculator.For(user.Points).Number;
        }
        catch (Exception e) { _sp.GetRequiredService<ErrorLog>().Write(e, "App bar avatar"); }
    }

    /// <summary>Back (the phones' back button, Android's system back): closes an open popup first, then goes to the
    /// previous screen. False at a tab root with nothing open, so Android leaves the app.</summary>
    public bool GoBack()
    {
        if (IsFeedbackOpen) { Feedback.CloseCommand.Execute(null); return true; }
        if (IsPopupOpen) { ClosePopup(); return true; }
        if (Current is IClosesOnBack screen && screen.CloseOnBack()) return true;
        if (_back.Count == 0) return false;
        var previous = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        Show(previous, previous.Open());
        return true;
    }


    public void ShowRegister() => Open(_sp.GetRequiredService<RegisterViewModel>, root: true);
    public void ShowLogin() => ShowLogin(notice: null);

    public void ShowLogin(string? notice) => Open(() =>
    {
        var vm = _sp.GetRequiredService<LoginViewModel>();
        if (notice is not null) vm.Notice = notice;
        return vm;
    }, root: true);

    public void ShowProfileSetup() => Open(_sp.GetRequiredService<ProfileSetupViewModel>, root: true);

    /// <summary><paramref name="afterSignUp"/>: the step before the profile setup; otherwise opened from Profile.
    /// <paramref name="sendCode"/>: nothing has been emailed yet.</summary>
    public void ShowConfirmEmail(bool afterSignUp = false, bool sendCode = false)
    {
        var first = sendCode;
        Open(() =>
        {
            var vm = _sp.GetRequiredService<ConfirmEmailViewModel>();
            vm.AfterSignUp = afterSignUp;
            // Only the first time; going back to the screen doesn't send another
            if (first) _ = vm.SendFirstCodeAsync();
            first = false;
            return vm;
        }, root: afterSignUp);
    }

    public void ShowForgotPassword(string? email = null) => Open(() =>
    {
        var vm = _sp.GetRequiredService<ForgotPasswordViewModel>();
        vm.Email = email ?? "";
        return vm;
    }, root: false);
    public void ShowHome() => Open(_sp.GetRequiredService<HomeViewModel>, root: true);
    public void ShowWorkouts() => Open(_sp.GetRequiredService<WorkoutsViewModel>, root: true);
    public void ShowCalories() => Open(_sp.GetRequiredService<CaloriesViewModel>, root: true);
    /// <summary>Friends, with the leaderboard on the same screen.</summary>
    /// <summary>Friends; a waiting invite code (an opened link) goes to its confirm card, once.</summary>
    public void ShowFriends() => Open(() =>
    {
        var vm = _sp.GetRequiredService<FriendsViewModel>();
        if (TakePendingInviteCode() is { } code) _ = vm.ShowInviteAsync(code);
        return vm;
    }, root: true);
    public void ShowSplits() => Open(_sp.GetRequiredService<SplitsViewModel>, root: true);

    public void ShowProfile() => ShowProfile(ProfilePage.Main);

    /// <summary>Profile on one of its phone pages (0.23.1): Calories and Home's setup step open Body data.</summary>
    public void ShowProfile(ProfilePage page) => Open(() =>
    {
        var vm = _sp.GetRequiredService<ProfileViewModel>();
        vm.Page = page;
        return vm;
    }, root: false);
    /// <summary>The Map screen: friends map, your gym, your check-ins. <paramref name="pickToday"/> opens the gym
    /// picker for today right away (Home's "Choose another…" and "Change gym").</summary>
    public void ShowGyms(bool pickToday = false, bool chooseCity = false, bool addGym = false, bool nearMe = false)
    {
        var first = true;
        Open(() =>
        {
            var vm = _sp.GetRequiredService<GymsViewModel>();
            // Only the first time; going back to the map doesn't open the picker again
            if (first && pickToday) vm.PickTodayWhenLoaded();
            if (first && chooseCity) vm.ChooseCityWhenLoaded();
            if (first && addGym) vm.AddGymWhenLoaded();
            if (first && nearMe) vm.FindNearbyWhenLoaded();   // on vacation: the city here and its gyms (19d)
            first = false;
            return vm;
        }, root: false);
    }

    /// <summary>Friend requests and what friends share. <paramref name="requests"/> opens on the Requests filter (a
    /// tapped friend request notification, Phase 22).</summary>
    public void ShowInbox(bool requests = false) => Open(() =>
    {
        var vm = _sp.GetRequiredService<InboxViewModel>();
        if (requests) vm.FilterIndex = InboxViewModel.RequestsFilter;
        _ = vm.LoadAsync();
        return vm;
    }, root: false);

    /// <summary>A friend notification was tapped (Phase 22): a request opens the Inbox's requests, an accepted request
    /// or invite opens Friends. Signed out, it's dropped: whoever signs in next may be someone else. On the UI thread.</summary>
    public void OpenFromPush(PushKind kind)
    {
        if (!IsSignedInScreen) return;
        if (kind == PushKind.FriendRequest) ShowInbox(requests: true);
        else ShowFriends();
    }

    /// <summary>A friend notification came while the app is open (Phase 22). The Inbox badge refreshes; Inbox and
    /// Friends reload in place. True when the head should still show it (any other screen). On the UI thread.</summary>
    public bool PushArrived(PushKind kind)
    {
        if (_session.CurrentUser is not { } user) return true;
        _ = _sp.GetRequiredService<InboxService>().RefreshUnreadAsync(user.Id);
        switch (Current)
        {
            case InboxViewModel inbox:
                _ = inbox.LoadAsync();
                return false;
            case FriendsViewModel friends:
                _ = friends.RefreshAsync();
                return false;
            default:
                return true;
        }
    }

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

    /// <summary>The new split popup; with a split, it edits that one.</summary>
    public void OpenSplitEditor(WorkoutSplit? split)
    {
        var vm = _sp.GetRequiredService<SplitEditorViewModel>();
        vm.Load(split);      // null = new split, otherwise pre-fills the form
        Popup = vm;
    }

    /// <summary>The new exercise popup; with an exercise, it edits that one. <paramref name="saved"/> runs after it
    /// closes; <paramref name="manage"/> is its link to the user's exercises, if it has one.</summary>
    public void OpenExerciseForm(Exercise? exercise, string subtitle, Func<Exercise, Task> saved, Action? manage = null)
    {
        var vm = _sp.GetRequiredService<ExerciseFormViewModel>();
        vm.Load(exercise, subtitle, saved, manage);
        Popup = vm;
    }

    /// <summary>The avatar popup. <paramref name="saved"/> runs after it closes.</summary>
    public void OpenAvatarPicker(string? avatar, string initials, int level, Func<Task> saved)
    {
        var vm = _sp.GetRequiredService<AvatarPickerViewModel>();
        vm.Load(avatar, initials, level, saved);
        Popup = vm;
    }

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
