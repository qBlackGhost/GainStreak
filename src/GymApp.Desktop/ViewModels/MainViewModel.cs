using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    public MainViewModel(SessionService session, IServiceProvider sp)
    {
        _session = session;
        _sp = sp;
        _session.Changed += Refresh;
        Refresh();
    }

    private void Refresh() =>
        Current = _session.IsLoggedIn
            ? _sp.GetRequiredService<HomeViewModel>()
            : _sp.GetRequiredService<LoginViewModel>();

    public void ShowRegister() => Current = _sp.GetRequiredService<RegisterViewModel>();
    public void ShowLogin() => Current = _sp.GetRequiredService<LoginViewModel>();
    public void ShowHome() => Current = _sp.GetRequiredService<HomeViewModel>();
    public void ShowSplits() => Current = _sp.GetRequiredService<SplitsViewModel>();
    public void ShowWorkouts() => Current = _sp.GetRequiredService<WorkoutsViewModel>();
    public void ShowProfile() => Current = _sp.GetRequiredService<ProfileViewModel>();
    public void ShowPointsHistory() => Current = _sp.GetRequiredService<PointsHistoryViewModel>();
    public void ShowGymPicker() => Current = _sp.GetRequiredService<GymPickerViewModel>();
    public void ShowFriends() => Current = _sp.GetRequiredService<FriendsViewModel>();

    public void ShowLeaderboard(bool fromFriends = false)
    {
        var vm = _sp.GetRequiredService<LeaderboardViewModel>();
        if (fromFriends) vm.OpenedFromFriends();
        Current = vm;
    }

    public void ShowGymSummary(bool fromProfile = false)
    {
        var vm = _sp.GetRequiredService<GymSummaryViewModel>();
        if (fromProfile) vm.OpenedFromProfile();
        Current = vm;
    }

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