using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Data.Models;
using GymApp.Services;

namespace GymApp.App.ViewModels;

public partial class MuscleChipViewModel(MuscleGroup group) : ObservableObject
{
    public MuscleGroup Group { get; } = group;
    public string Label { get; } = group.ToString();

    [ObservableProperty] public partial bool IsSelected { get; set; }
}

/// <summary>The popup that creates one of the user's own exercises or edits one: its name and muscle group.</summary>
public partial class ExerciseFormViewModel : ViewModelBase
{
    private readonly ExerciseService _exercises;
    private readonly SessionService _session;
    private readonly Func<MainViewModel> _shell;
    private int? _editingId;
    private Func<Exercise, Task> _saved = _ => Task.CompletedTask;
    private Action? _manage;

    public IReadOnlyList<MuscleChipViewModel> Groups { get; } =
        Enum.GetValues<MuscleGroup>().Select(g => new MuscleChipViewModel(g)).ToList();

    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial string Title { get; set; } = "New exercise";
    [ObservableProperty] public partial string Subtitle { get; set; } = "";
    [ObservableProperty] public partial string SaveLabel { get; set; } = "Create exercise";
    [ObservableProperty] public partial bool CanManage { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }
    [ObservableProperty] public partial string? NameError { get; set; }
    [ObservableProperty] public partial string? GroupError { get; set; }

    public int MaxNameLength => ExerciseService.MaxNameLength;

    public ExerciseFormViewModel(ExerciseService exercises, SessionService session, Func<MainViewModel> shell)
    {
        _exercises = exercises; _session = session; _shell = shell;
    }

    /// <summary>Call right after resolving; a null exercise = creating a new one.</summary>
    public void Load(Exercise? exercise, string subtitle, Func<Exercise, Task> saved, Action? manage)
    {
        _editingId = exercise?.Id;
        _saved = saved;
        _manage = manage;
        Subtitle = subtitle;
        CanManage = manage is not null;
        if (exercise is null) return;

        Title = "Edit exercise";
        SaveLabel = "Save changes";
        Name = exercise.Name;
        Groups.First(g => g.Group == exercise.MuscleGroup).IsSelected = true;
    }

    [RelayCommand]
    private void SelectGroup(MuscleChipViewModel chip)
    {
        foreach (var g in Groups) g.IsSelected = g == chip;
        GroupError = null;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        Error = NameError = GroupError = null;
        if (Groups.FirstOrDefault(g => g.IsSelected) is not { } chip) { GroupError = "Pick a muscle group."; return; }

        var userId = _session.CurrentUser!.Id;
        var name = (Name ?? "").Trim();
        ExerciseResult r;
        try
        {
            r = _editingId is { } id
                ? await _exercises.UpdateAsync(userId, id, name, chip.Group)
                : await _exercises.CreateAsync(userId, name, chip.Group);
        }
        catch (Exception)
        {
            Error = "Could not save the exercise.";
            return;
        }

        if (r.Success)
        {
            _shell().ClosePopup();
            await _saved(r.Exercise!);
            return;
        }
        switch (r.Field)
        {
            case FormField.Name: NameError = r.Error; break;
            case FormField.MuscleGroup: GroupError = r.Error; break;
            default: Error = r.Error; break;
        }
    }

    [RelayCommand] private void Cancel() => _shell().ClosePopup();
    [RelayCommand] private void Manage() => _manage?.Invoke();
}
