using System;
using CommunityToolkit.Mvvm.ComponentModel;
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