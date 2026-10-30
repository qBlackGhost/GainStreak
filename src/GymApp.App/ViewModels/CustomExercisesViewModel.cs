using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Data.Models;
using GymApp.Services;

namespace GymApp.App.ViewModels;

/// <summary>One of the user's own exercises, with where it's used.</summary>
public partial class CustomExerciseItemViewModel(Exercise exercise, ExerciseUsage usage, CustomExercisesViewModel owner) : ObservableObject
{
    public Exercise Exercise { get; } = exercise;
    public string Name => Exercise.Name;
    public string UsageText { get; } = Describe(usage);

    private static string Describe(ExerciseUsage usage)
    {
        var days = usage.DayLists switch
        {
            0 => "Not in a day yet",
            1 => "In 1 day",
            var n => $"In {n} days",
        };
        return usage.Sets switch
        {
            0 => days,
            1 => $"{days} · 1 set",
            var n => $"{days} · {n} sets",
        };
    }

    [RelayCommand] private void Edit() => owner.StartEdit(this);
    [RelayCommand] private Task Delete() => owner.RequestDeleteAsync(this);
}

public record CustomExerciseGroup(string Title, IReadOnlyList<CustomExerciseItemViewModel> Items);

public partial class CustomExercisesViewModel : ViewModelBase, IClosesOnBack
{
    private readonly ExerciseService _exercises;
    private readonly SessionService _session;
    private readonly Func<MainViewModel> _shell;
    private MenuDay? _day;
    private string _splitName = "";
    private List<CustomExerciseItemViewModel> _all = [];
    private CustomExerciseItemViewModel? _deleting;

    public ObservableCollection<CustomExerciseGroup> Groups { get; } = new();

    [ObservableProperty] public partial string Search { get; set; } = "";
    [ObservableProperty] public partial string CountShort { get; set; } = "";
    [ObservableProperty] public partial string CountLabel { get; set; } = "";
    [ObservableProperty] public partial bool IsEmpty { get; set; }
    [ObservableProperty] public partial bool HasExercises { get; set; }
    [ObservableProperty] public partial bool NoMatches { get; set; }
    [ObservableProperty] public partial bool CanAdd { get; set; } = true;
    [ObservableProperty] public partial bool IsAtLimit { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }
    [ObservableProperty] public partial string? Info { get; set; }

    [ObservableProperty] public partial bool IsConfirmingDelete { get; set; }
    [ObservableProperty] public partial string DeleteTitle { get; set; } = "";
    [ObservableProperty] public partial string DeleteText { get; set; } = "";

    private int UserId => _session.CurrentUser!.Id;

    public CustomExercisesViewModel(ExerciseService exercises, SessionService session, Func<MainViewModel> shell)
    {
        _exercises = exercises; _session = session; _shell = shell;
    }

    /// <summary>Call right after resolving from DI. The day is only remembered so Back returns to it.</summary>
    public void Load(MenuDay day, string splitName)
    {
        _day = day;
        _splitName = splitName;
        _ = ReloadAsync();
    }

    public bool CloseOnBack()
    {
        if (!IsConfirmingDelete) return false;
        IsConfirmingDelete = false;
        return true;
    }

    public async Task ReloadAsync()
    {
        try
        {
            var custom = await _exercises.GetCustomAsync(UserId);
            var usages = await _exercises.GetUsagesAsync(UserId);
            _all = custom.Select(e => new CustomExerciseItemViewModel(e, usages.GetValueOrDefault(e.Id, new ExerciseUsage(0, 0)), this)).ToList();

            IsEmpty = custom.Count == 0;
            HasExercises = !IsEmpty;
            CanAdd = custom.Count < ExerciseService.MaxCustomExercises;
            IsAtLimit = !CanAdd;
            CountShort = $"{custom.Count} / {ExerciseService.MaxCustomExercises}";
            CountLabel = $"{custom.Count} / {ExerciseService.MaxCustomExercises} custom exercises";
            ShowGroups();
        }
        catch (Exception)
        {
            Error = "Could not load your exercises.";
        }
    }

    partial void OnSearchChanged(string value) => ShowGroups();

    /// <summary>By muscle group (enum order), then by name, like the day's picker.</summary>
    private void ShowGroups()
    {
        var query = Search.Trim();
        var groups = _all
            .Where(i => i.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .GroupBy(i => i.Exercise.MuscleGroup)
            .OrderBy(g => g.Key)
            .Select(g => new CustomExerciseGroup(g.Key.ToString().ToUpperInvariant(), g.OrderBy(i => i.Name).ToList()))
            .ToList();
        Groups.Clear();
        foreach (var g in groups) Groups.Add(g);
        NoMatches = HasExercises && groups.Count == 0;
    }

    [RelayCommand]
    private void New() => _shell().OpenExerciseForm(null, "Saved to your exercises",
        e => SavedAsync($"Added {e.Name}. Go back to the day to put it in your list."));

    internal void StartEdit(CustomExerciseItemViewModel item)
    {
        Error = null; Info = null;
        _shell().OpenExerciseForm(item.Exercise, "Changes show everywhere it's used", e => SavedAsync($"Saved {e.Name}."));
    }

    private Task SavedAsync(string info)
    {
        Info = info;
        return ReloadAsync();
    }

    internal async Task RequestDeleteAsync(CustomExerciseItemViewModel item)
    {
        ExerciseUsage usage;
        try { usage = await _exercises.GetUsageAsync(UserId, item.Exercise.Id); }
        catch (Exception) { Error = "Could not check where that exercise is used."; return; }

        _deleting = item;
        DeleteTitle = $"Delete {item.Name}?";
        DeleteText = DeleteWarning(usage);
        IsConfirmingDelete = true;
        Error = null; Info = null;
    }

    private static string DeleteWarning(ExerciseUsage usage)
    {
        if (usage.DayLists == 0 && usage.Sets == 0)
            return "It isn't in any of your days and has no logged sets.";

        var parts = new List<string>();
        if (usage.DayLists > 0)
            parts.Add(usage.DayLists == 1 ? "removed from 1 day list" : $"removed from {usage.DayLists} day lists");
        if (usage.Sets > 0)
            parts.Add(usage.Sets == 1 ? "its 1 logged set deleted" : $"its {usage.Sets} logged sets deleted");

        return $"It will be {string.Join(" and ", parts)}, and its PR history goes with it. This can't be undone.";
    }

    [RelayCommand] private void CancelDelete() => IsConfirmingDelete = false;

    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        IsConfirmingDelete = false;
        if (_deleting is not { } item) return;

        bool deleted;
        try { deleted = await _exercises.DeleteAsync(UserId, item.Exercise.Id); }
        catch (Exception) { deleted = false; }
        if (!deleted)
        {
            Error = "Could not delete that exercise.";
            return;
        }

        Error = null;
        Info = $"Deleted {item.Name}.";
        await ReloadAsync();
    }

    [RelayCommand]
    private void Back()
    {
        if (_day is null) _shell().ShowWorkouts();
        else _shell().ShowDay(_day, _splitName);   // reloads the picker, so new exercises show up
    }
}
