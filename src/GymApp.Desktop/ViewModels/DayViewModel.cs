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

public partial class DayExerciseItemViewModel : ObservableObject
{
    private readonly DayViewModel _owner;

    public DayExerciseItemViewModel(UserDayExercise row, int index, int count, DayViewModel owner)
    {
        _owner = owner;
        ExerciseId = row.ExerciseId;
        Number = index + 1;
        Name = row.Exercise.Name;
        Group = row.Exercise.MuscleGroup.ToString();
        Planned = row.WarmupSets > 0
            ? $"{row.WarmupSets} warm-up · {row.WorkingSets} working set(s)"
            : $"{row.WorkingSets} working set(s)";
        CanMoveUp = index > 0;
        CanMoveDown = index < count - 1;
    }

    public int ExerciseId { get; }
    public int Number { get; }
    public string Name { get; }
    public string Group { get; }
    public string Planned { get; }
    public bool CanMoveUp { get; }
    public bool CanMoveDown { get; }

    [RelayCommand] private Task MoveUp() => _owner.MoveAsync(this, -1);
    [RelayCommand] private Task MoveDown() => _owner.MoveAsync(this, +1);
    [RelayCommand] private Task Remove() => _owner.RemoveAsync(this);
}

public partial class PickerItemViewModel : ObservableObject
{
    private readonly DayViewModel _owner;
    public PickerItemViewModel(Exercise exercise, DayViewModel owner) { ExerciseId = exercise.Id; Name = exercise.Name; _owner = owner; }

    public int ExerciseId { get; }
    public string Name { get; }

    [RelayCommand] private Task Add() => _owner.AddAsync(this);
}

public record PickerGroup(string Name, IReadOnlyList<PickerItemViewModel> Items);

public partial class CopySourceItemViewModel : ObservableObject
{
    private readonly DayViewModel _owner;
    public CopySourceItemViewModel(CopySource source, DayViewModel owner) { Source = source; _owner = owner; }

    public CopySource Source { get; }
    public string Label => $"{Source.DayName} · {Source.SplitName}";
    public string CountText => Source.ExerciseCount == 1 ? "1 exercise" : $"{Source.ExerciseCount} exercises";

    [RelayCommand] private Task Choose() => _owner.ChooseCopyAsync(this);
}

public partial class DayViewModel : ViewModelBase
{
    private readonly DayExerciseService _days;
    private readonly ExerciseService _exercises;
    private readonly SessionService _session;
    private readonly Func<MainViewModel> _shell;
    private int _dayId;
    private List<Exercise> _catalog = [];

    public ObservableCollection<DayExerciseItemViewModel> Items { get; } = new();
    public ObservableCollection<PickerGroup> PickerGroups { get; } = new();
    public ObservableCollection<CopySourceItemViewModel> CopySources { get; } = new();

    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial string Subtitle { get; set; } = "";
    [ObservableProperty] public partial string? Error { get; set; }
    [ObservableProperty] public partial string? Info { get; set; }
    [ObservableProperty] public partial bool IsEmpty { get; set; }

    [ObservableProperty] public partial bool IsPickerOpen { get; set; }
    [ObservableProperty] public partial string Search { get; set; } = "";
    [ObservableProperty] public partial bool NoPickerResults { get; set; }

    [ObservableProperty] public partial bool IsCopyOpen { get; set; }
    [ObservableProperty] public partial bool HasCopySources { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingCopy))]
    [NotifyPropertyChangedFor(nameof(PendingCopyText))]
    public partial CopySourceItemViewModel? PendingCopy { get; set; }

    public bool HasPendingCopy => PendingCopy is not null;
    public string PendingCopyText => PendingCopy is null ? "" :
        $"Copy {PendingCopy.CountText} from {PendingCopy.Label} into {Title}? " +
        "Planned sets come along; logged sets and PRs never do.";

    private int UserId => _session.CurrentUser!.Id;

    public DayViewModel(DayExerciseService days, ExerciseService exercises, SessionService session, Func<MainViewModel> shell)
    {
        _days = days; _exercises = exercises; _session = session; _shell = shell;
    }

    /// <summary>Call right after resolving from DI.</summary>
    public void Load(MenuDay day, string splitName)
    {
        _dayId = day.SplitDayId;
        Title = day.Name;
        Subtitle = day.Weekdays.Length > 0 ? $"{splitName} · {day.Weekdays}" : splitName;
        _ = ReloadAsync(reloadCatalog: true);
    }

    private async Task ReloadAsync(bool reloadCatalog = false)
    {
        try
        {
            var rows = await _days.GetListAsync(UserId, _dayId);
            Items.Clear();
            for (var i = 0; i < rows.Count; i++)
                Items.Add(new DayExerciseItemViewModel(rows[i], i, rows.Count, this));
            IsEmpty = rows.Count == 0;

            if (reloadCatalog) _catalog = await _exercises.GetAvailableAsync(UserId);
            RebuildPicker();
        }
        catch (Exception)
        {
            Error = "Could not load this day.";
        }
    }

    // The picker lists the exercises not yet in this day, grouped by muscle group (the catalog is already in enum order)
    private void RebuildPicker()
    {
        var inDay = Items.Select(i => i.ExerciseId).ToHashSet();
        var term = Search?.Trim();
        var groups = _catalog
            .Where(e => !inDay.Contains(e.Id))
            .Where(e => string.IsNullOrEmpty(term) || e.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => e.MuscleGroup)
            .Select(g => new PickerGroup(g.Key.ToString(), g.Select(e => new PickerItemViewModel(e, this)).ToList()))
            .ToList();

        PickerGroups.Clear();
        foreach (var g in groups) PickerGroups.Add(g);
        NoPickerResults = groups.Count == 0;
    }

    partial void OnSearchChanged(string value) => RebuildPicker();

    internal async Task AddAsync(PickerItemViewModel item)
    {
        var r = await _days.AddAsync(UserId, _dayId, item.ExerciseId);
        if (!r.Success) { Error = r.Error; Info = null; return; }

        Error = null;
        Info = $"Added {item.Name}.";
        await ReloadAsync();      // the picker stays open so several can be added in a row
    }

    internal async Task RemoveAsync(DayExerciseItemViewModel item)
    {
        if (!await _days.RemoveAsync(UserId, _dayId, item.ExerciseId))
        {
            Error = "Could not remove that exercise.";
            return;
        }

        Error = null;
        Info = $"Removed {item.Name}. Its logged sets and PR are kept.";
        await ReloadAsync();
    }

    internal async Task MoveAsync(DayExerciseItemViewModel item, int direction)
    {
        await _days.MoveAsync(UserId, _dayId, item.ExerciseId, direction);
        Error = null; Info = null;
        await ReloadAsync();
    }

    [RelayCommand]
    private void TogglePicker()
    {
        IsPickerOpen = !IsPickerOpen;
        if (IsPickerOpen) { IsCopyOpen = false; PendingCopy = null; }
    }

    [RelayCommand]
    private async Task ToggleCopyAsync()
    {
        IsCopyOpen = !IsCopyOpen;
        PendingCopy = null;
        if (!IsCopyOpen) return;
        IsPickerOpen = false;

        var sources = await _days.GetCopySourcesAsync(UserId, _dayId);
        CopySources.Clear();
        foreach (var s in sources) CopySources.Add(new CopySourceItemViewModel(s, this));
        HasCopySources = sources.Count > 0;
    }

    // An empty day just receives the copy; a day with exercises asks first (add to the end or replace)
    internal Task ChooseCopyAsync(CopySourceItemViewModel item)
    {
        if (Items.Count == 0) return CopyAsync(item, CopyMode.Append);
        PendingCopy = item;
        return Task.CompletedTask;
    }

    [RelayCommand] private Task ConfirmAppendAsync() =>
        PendingCopy is { } p ? CopyAsync(p, CopyMode.Append) : Task.CompletedTask;

    [RelayCommand] private Task ConfirmReplaceAsync() =>
        PendingCopy is { } p ? CopyAsync(p, CopyMode.Replace) : Task.CompletedTask;

    [RelayCommand] private void CancelCopy() => PendingCopy = null;

    private async Task CopyAsync(CopySourceItemViewModel item, CopyMode mode)
    {
        var r = await _days.CopyAsync(UserId, item.Source.SplitDayId, _dayId, mode);
        if (!r.Success) { Error = r.Error; Info = null; return; }

        Error = null;
        Info = r.Skipped > 0
            ? $"Copied {r.Added} exercise(s), skipped {r.Skipped} already in the list."
            : $"Copied {r.Added} exercise(s).";
        PendingCopy = null;
        IsCopyOpen = false;
        await ReloadAsync();
    }

    [RelayCommand] private void Back() => _shell().ShowWorkouts();
}