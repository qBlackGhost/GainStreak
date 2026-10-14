using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Data.Models;
using GymApp.Services;

namespace GymApp.Desktop.ViewModels;

/// <summary>One of the user's own exercises. Deleting asks first and says what goes with it.</summary>
public partial class CustomExerciseItemViewModel : ObservableObject
{
    private readonly CustomExercisesViewModel _owner;

    public CustomExerciseItemViewModel(Exercise exercise, CustomExercisesViewModel owner)
    {
        Exercise = exercise;
        _owner = owner;
    }

    public Exercise Exercise { get; }
    public string Name => Exercise.Name;
    public string Group => Exercise.MuscleGroup.ToString();

    [ObservableProperty] public partial bool IsConfirmingDelete { get; set; }
    [ObservableProperty] public partial string ConfirmText { get; set; } = "";

    [RelayCommand] private void Edit() => _owner.StartEdit(this);
    [RelayCommand] private Task Delete() => _owner.RequestDeleteAsync(this);
    [RelayCommand] private Task ConfirmDelete() => _owner.DeleteAsync(this);
    [RelayCommand] private void CancelDelete() => IsConfirmingDelete = false;
}

public partial class CustomExercisesViewModel : ViewModelBase
{
    private readonly ExerciseService _exercises;
    private readonly SessionService _session;
    private readonly Func<MainViewModel> _shell;
    private MenuDay? _day;
    private string _splitName = "";
    private int? _editingId;

    public ObservableCollection<CustomExerciseItemViewModel> Items { get; } = new();
    public IReadOnlyList<MuscleGroup> Groups { get; } = Enum.GetValues<MuscleGroup>();

    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial MuscleGroup? SelectedGroup { get; set; }

    [ObservableProperty] public partial string FormTitle { get; set; } = "New exercise";
    [ObservableProperty] public partial string SaveLabel { get; set; } = "Add exercise";
    [ObservableProperty] public partial bool IsEditing { get; set; }

    [ObservableProperty] public partial string Counter { get; set; } = "";
    [ObservableProperty] public partial bool IsEmpty { get; set; }
    [ObservableProperty] public partial bool CanAdd { get; set; } = true;
    [ObservableProperty] public partial string? Error { get; set; }
    [ObservableProperty] public partial string? Info { get; set; }

    public int MaxNameLength => ExerciseService.MaxNameLength;

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

    private async Task ReloadAsync()
    {
        try
        {
            var custom = await _exercises.GetCustomAsync(UserId);
            Items.Clear();
            foreach (var e in custom) Items.Add(new CustomExerciseItemViewModel(e, this));

            IsEmpty = custom.Count == 0;
            CanAdd = custom.Count < ExerciseService.MaxCustomExercises || IsEditing;
            Counter = $"{custom.Count} / {ExerciseService.MaxCustomExercises} custom exercises";
        }
        catch (Exception)
        {
            Error = "Could not load your exercises.";
        }
    }

    internal void StartEdit(CustomExerciseItemViewModel item)
    {
        _editingId = item.Exercise.Id;
        Name = item.Name;
        SelectedGroup = item.Exercise.MuscleGroup;
        FormTitle = "Edit exercise";
        SaveLabel = "Save changes";
        IsEditing = true;
        CanAdd = true;
        Error = null; Info = null;
        foreach (var i in Items) i.IsConfirmingDelete = false;
    }

    private void ResetForm()
    {
        _editingId = null;
        Name = "";
        SelectedGroup = null;
        FormTitle = "New exercise";
        SaveLabel = "Add exercise";
        IsEditing = false;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        Info = null;
        if (SelectedGroup is not { } group) { Error = "Pick a muscle group."; return; }

        var name = (Name ?? "").Trim();
        var r = _editingId is { } id
            ? await _exercises.UpdateAsync(UserId, id, name, group)
            : await _exercises.CreateAsync(UserId, name, group);

        if (!r.Success) { Error = r.Error; return; }

        Error = null;
        Info = _editingId is null
            ? $"Added {r.Exercise!.Name}. Go back to the day to put it in your list."
            : $"Saved {r.Exercise!.Name}.";
        ResetForm();
        await ReloadAsync();
    }

    [RelayCommand]
    private void CancelEdit()
    {
        ResetForm();
        Error = null; Info = null;
        CanAdd = Items.Count < ExerciseService.MaxCustomExercises;
    }

    internal async Task RequestDeleteAsync(CustomExerciseItemViewModel item)
    {
        foreach (var i in Items) i.IsConfirmingDelete = false;

        var usage = await _exercises.GetUsageAsync(UserId, item.Exercise.Id);
        item.ConfirmText = DeleteWarning(item.Name, usage);
        item.IsConfirmingDelete = true;
        Error = null; Info = null;
    }

    private static string DeleteWarning(string name, ExerciseUsage usage)
    {
        if (usage.DayLists == 0 && usage.Sets == 0)
            return $"Delete {name}? It isn't in any of your days and has no logged sets.";

        var parts = new List<string>();
        if (usage.DayLists > 0)
            parts.Add(usage.DayLists == 1 ? "removed from 1 day list" : $"removed from {usage.DayLists} day lists");
        if (usage.Sets > 0)
            parts.Add(usage.Sets == 1 ? "its 1 logged set deleted" : $"its {usage.Sets} logged sets deleted");

        return $"Delete {name}? It will be {string.Join(" and ", parts)}, and its PR history goes with it. This can't be undone.";
    }

    internal async Task DeleteAsync(CustomExerciseItemViewModel item)
    {
        if (!await _exercises.DeleteAsync(UserId, item.Exercise.Id))
        {
            Error = "Could not delete that exercise.";
            return;
        }

        // Deleting the exercise that is open in the form would leave the form pointing at nothing
        if (_editingId == item.Exercise.Id) ResetForm();

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
