using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Contracts;
using GymApp.Services;
using Microsoft.Extensions.DependencyInjection;
using GymApp.Data.Models;

namespace GymApp.Desktop.ViewModels;

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
        Current = _session.IsLoggedIn
            ? _sp.GetRequiredService<HomeViewModel>()
            : _sp.GetRequiredService<LoginViewModel>();
    }

    public void ShowRegister() => Current = _sp.GetRequiredService<RegisterViewModel>();
    public void ShowLogin() => Current = _sp.GetRequiredService<LoginViewModel>();
    public void ShowHome() => Current = _sp.GetRequiredService<HomeViewModel>();
    public void ShowSplits() => Current = _sp.GetRequiredService<SplitsViewModel>();
    public void ShowWorkouts() => Current = _sp.GetRequiredService<WorkoutsViewModel>();
    public void ShowProfile() => Current = _sp.GetRequiredService<ProfileViewModel>();
    public void ShowCalories() => Current = _sp.GetRequiredService<CaloriesViewModel>();
    /// <summary>The Map screen: friends map, your gym, your check-ins. <paramref name="pickToday"/> opens the gym
    /// picker for today right away (Home's "Choose another…" and "Change gym").</summary>
    public void ShowGyms(bool pickToday = false, bool chooseCity = false)
    {
        var vm = _sp.GetRequiredService<GymsViewModel>();
        if (pickToday) vm.PickTodayWhenLoaded();
        if (chooseCity) vm.ChooseCityWhenLoaded();
        Current = vm;
    }
    /// <summary>Friends, with the leaderboard on the same screen.</summary>
    public void ShowFriends() => Current = _sp.GetRequiredService<FriendsViewModel>();

    public void ShowDay(MenuDay day, string splitName)
    {
        var vm = _sp.GetRequiredService<DayViewModel>();
        vm.Load(day, splitName);
        Current = vm;
    }

    public void ShowExercise(MenuDay day, string splitName, int exerciseId)
    {
        var vm = _sp.GetRequiredService<ExerciseViewModel>();
        vm.Load(day, splitName, exerciseId);
        Current = vm;
    }

    public void ShowCustomExercises(MenuDay day, string splitName)
    {
        var vm = _sp.GetRequiredService<CustomExercisesViewModel>();
        vm.Load(day, splitName);
        Current = vm;
    }

    public void ShowSplitEditor(WorkoutSplit? split)
    {
        var vm = _sp.GetRequiredService<SplitEditorViewModel>();
        vm.Load(split);      // null = new split, otherwise pre-fills the form
        Current = vm;
    }

    public void ShowSplitDetail(WorkoutSplit split)
    {
        var vm = _sp.GetRequiredService<SplitDetailViewModel>();
        vm.Load(split);
        Current = vm;
    }
}