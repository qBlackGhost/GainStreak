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

public partial class DayExerciseItemViewModel : ObservableObject
{
    private readonly DayViewModel _owner;

    public DayExerciseItemViewModel(UserDayExercise row, int index, int count, DayViewModel owner)
    {
        _owner = owner;
        ExerciseId = row.ExerciseId;
        Number = index + 1;
        Name = row.Exercise.Name;
        Group = row.Exercise.OwnerUserId is null ? row.Exercise.MuscleGroup.ToString() : "Custom";
        Planned = row.WarmupSets > 0
            ? $"{row.WarmupSets} warm-up · {row.WorkingSets} working"
            : $"{row.WorkingSets} working";
        Sets = row.WarmupSets + row.WorkingSets;
        Index = index;
        CanMoveUp = index > 0;
        CanMoveDown = index < count - 1;
    }

    public int ExerciseId { get; }
    public int Index { get; }
    public int Number { get; }
    public int Sets { get; }
    public string Name { get; }
    public string Group { get; }
    public string Planned { get; }
    public bool CanMoveUp { get; }
    public bool CanMoveDown { get; }

    // While dragging (DayView.axaml.cs): the dragged row fades, the drop target shows a line above or below
    [ObservableProperty] public partial bool IsDragging { get; set; }
    [ObservableProperty] public partial bool IsDropAbove { get; set; }
    [ObservableProperty] public partial bool IsDropBelow { get; set; }

    [RelayCommand] private Task MoveUp() => _owner.MoveAsync(this, -1);
    [RelayCommand] private Task MoveDown() => _owner.MoveAsync(this, +1);
    [RelayCommand] private Task Remove() => _owner.RemoveAsync(this);
    [RelayCommand] private void Open() => _owner.OpenExercise(this);
}

/// <summary>One tile in the picker. Exercises already in the day stay visible but can't be clicked;
/// the button's disabled look is what greys them out.</summary>
public partial class PickerItemViewModel : ObservableObject
{
    private readonly DayViewModel _owner;

    public PickerItemViewModel(Exercise exercise, bool isAdded, DayViewModel owner)
    {
        ExerciseId = exercise.Id;
        Name = exercise.Name;
        IsAdded = isAdded;
        _owner = owner;
    }

    public int ExerciseId { get; }
    public string Name { get; }
    public bool IsAdded { get; }
    public string Glyph => IsAdded ? "✓" : "+";

    private bool CanAdd => !IsAdded;

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private Task Add() => _owner.AddAsync(this);
}

public record PickerGroup(string Name, IReadOnlyList<PickerItemViewModel> Items)
{
    public string Title => Name.ToUpperInvariant();
}

/// <summary>A muscle group chip above the picker. Group null = "All".</summary>
public partial class GroupFilter(string label, MuscleGroup? group) : ObservableObject
{
    public string Label { get; } = label;
    public MuscleGroup? Group { get; } = group;

    [ObservableProperty] public partial bool IsSelected { get; set; }
}

public partial class CopySourceItemViewModel : ObservableObject
{
    private readonly DayViewModel _owner;
    public CopySourceItemViewModel(CopySource source, DayViewModel owner) { Source = source; _owner = owner; }

    public CopySource Source { get; }
    public string Label => $"{Source.DayName} · {Source.SplitName}";
    public string CountText => Source.ExerciseCount == 1 ? "1 exercise" : $"{Source.ExerciseCount} exercises";

    /// <summary>The source picked in the copy dialog.</summary>
    [ObservableProperty] public partial bool IsSelected { get; set; }

    [RelayCommand] private void Choose() => _owner.ChooseCopy(this);
}

public partial class DayViewModel : ViewModelBase, IClosesOnBack
{
    /// <summary>Back closes "Copy from another day" or the phones' add-exercises sheet first.</summary>
    public bool CloseOnBack()
    {
        if (IsCopyOpen) { IsCopyOpen = false; return true; }
        if (IsAddOpen) { IsAddOpen = false; return true; }
        return false;
    }

    /// <summary>The phones' add-exercises sheet (the GainStreak Mobile canvas); wider screens show the picker beside
    /// the list all the time.</summary>
    [ObservableProperty] public partial bool IsAddOpen { get; set; }

    [RelayCommand] private void OpenAdd() => IsAddOpen = true;
    [RelayCommand] private void CloseAdd() => IsAddOpen = false;

    private readonly DayExerciseService _days;
    private readonly ExerciseService _exercises;
    private readonly SessionService _session;
    private readonly Func<MainViewModel> _shell;
    private int _dayId;
    private MenuDay? _menuDay;
    private string _splitName = "";
    private List<Exercise> _catalog = [];

    public ObservableCollection<DayExerciseItemViewModel> Items { get; } = new();
    public ObservableCollection<PickerGroup> PickerGroups { get; } = new();
    public ObservableCollection<CopySourceItemViewModel> CopySources { get; } = new();

    public IReadOnlyList<GroupFilter> Filters { get; } =
        new[] { new GroupFilter("All", null) }
            .Concat(Enum.GetValues<MuscleGroup>().Select(g => new GroupFilter(g.ToString(), g)))
            .ToList();

    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial string Subtitle { get; set; } = "";
    [ObservableProperty] public partial string CountText { get; set; } = "";
    [ObservableProperty] public partial string CountLabel { get; set; } = "";
    [ObservableProperty] public partial string SetsLabel { get; set; } = "";
    [ObservableProperty] public partial string WeekdaysText { get; set; } = "";
    [ObservableProperty] public partial string? Error { get; set; }
    [ObservableProperty] public partial string? Info { get; set; }
    [ObservableProperty] public partial bool IsEmpty { get; set; }

    [ObservableProperty] public partial string Search { get; set; } = "";
    [ObservableProperty] public partial GroupFilter? SelectedFilter { get; set; }
    [ObservableProperty] public partial bool NoPickerResults { get; set; }

    [ObservableProperty] public partial bool IsCopyOpen { get; set; }
    [ObservableProperty] public partial bool HasCopySources { get; set; }

    /// <summary>The source picked in the copy dialog; "Copy exercises" waits for one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingCopy))]
    public partial CopySourceItemViewModel? PendingCopy { get; set; }

    public bool HasPendingCopy => PendingCopy is not null;

    /// <summary>A day that already has exercises asks whether to add to the end or replace the list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAppendMode))]
    public partial bool IsReplaceMode { get; set; }

    public bool IsAppendMode => !IsReplaceMode;
    public string CopyModeQuestion => $"{Title} already has exercises. What should happen?";
    public string CopySkipNote => $"Exercises already in {Title} are skipped, so nothing is duplicated.";

    private int UserId => _session.CurrentUser!.Id;

    public DayViewModel(DayExerciseService days, ExerciseService exercises, SessionService session, Func<MainViewModel> shell)
    {
        _days = days; _exercises = exercises; _session = session; _shell = shell;
        SelectedFilter = Filters[0];
        Filters[0].IsSelected = true;
    }

    /// <summary>Call right after resolving from DI.</summary>
    public void Load(MenuDay day, string splitName)
    {
        _menuDay = day;
        _splitName = splitName;
        _dayId = day.SplitDayId;
        Title = day.Name;
        Subtitle = splitName;
        WeekdaysText = day.Weekdays;
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
            CountText = rows.Count == 1 ? "1 exercise" : $"{rows.Count} exercises";
            CountLabel = $"{rows.Count} / {DayExerciseService.MaxExercisesPerDay} exercises";
            var sets = Items.Sum(i => i.Sets);
            SetsLabel = rows.Count == 0 ? "Nothing planned yet" : $"{CountText} · {sets} planned sets";

            if (reloadCatalog) _catalog = await _exercises.GetAvailableAsync(UserId);
            RebuildPicker();
        }
        catch (Exception)
        {
            Error = "Could not load this day.";
        }
    }

    // The picker lists every exercise in the selected group (or all of them), narrowed by the search box.
    // The ones already in this day stay in place, marked as added, so the list never reshuffles.
    private void RebuildPicker()
    {
        var inDay = Items.Select(i => i.ExerciseId).ToHashSet();
        var term = Search?.Trim();
        var group = SelectedFilter?.Group;
        var groups = _catalog
            .Where(e => group is null || e.MuscleGroup == group)
            .Where(e => string.IsNullOrEmpty(term) || e.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => e.MuscleGroup)
            .Select(g => new PickerGroup(
                g.Key.ToString(),
                g.Select(e => new PickerItemViewModel(e, inDay.Contains(e.Id), this)).ToList()))
            .ToList();

        PickerGroups.Clear();
        foreach (var g in groups) PickerGroups.Add(g);
        NoPickerResults = groups.Count == 0;
    }

    partial void OnSearchChanged(string value) => RebuildPicker();

    partial void OnSelectedFilterChanged(GroupFilter? value)
    {
        if (value is null) { SelectedFilter = Filters[0]; return; }   // never leave the filter empty
        foreach (var f in Filters) f.IsSelected = f == value;
        RebuildPicker();
    }

    [RelayCommand] private void SelectFilter(GroupFilter filter) => SelectedFilter = filter;

    internal async Task AddAsync(PickerItemViewModel item)
    {
        try
        {
            var r = await _days.AddAsync(UserId, _dayId, item.ExerciseId);
            if (!r.Success) { Error = r.Error; Info = null; return; }

            // Go straight to the new exercise so its sets can be set up right away
            _shell().ShowExercise(_menuDay!, _splitName, item.ExerciseId);
        }
        catch (Exception)
        {
            Error = "Could not add that exercise.";
        }
    }

    internal async Task RemoveAsync(DayExerciseItemViewModel item)
    {
        try
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
        catch (Exception)
        {
            Error = "Could not remove that exercise.";
        }
    }

    internal async Task MoveAsync(DayExerciseItemViewModel item, int direction)
    {
        try
        {
            await _days.MoveAsync(UserId, _dayId, item.ExerciseId, direction);
            Error = null; Info = null;
            await ReloadAsync();

        }
        catch (Exception)
        {
            Error = "Could not move that exercise.";
        }
    }

    /// <summary>Drag and drop: moves the exercise to <paramref name="index"/> in one step.</summary>
    internal async Task MoveToAsync(DayExerciseItemViewModel item, int index)
    {
        if (index == item.Index) return;
        try
        {
            await _days.MoveToAsync(UserId, _dayId, item.ExerciseId, index);
            Error = null; Info = null;
            await ReloadAsync();
        }
        catch (Exception)
        {
            Error = "Could not move that exercise.";
        }
    }

    internal void OpenExercise(DayExerciseItemViewModel item) =>
        _shell().ShowExercise(_menuDay!, _splitName, item.ExerciseId);

    [RelayCommand]
    private async Task ToggleCopyAsync()
    {
        try
        {
            IsCopyOpen = !IsCopyOpen;
            PendingCopy = null;
            if (!IsCopyOpen) return;

            var sources = await _days.GetCopySourcesAsync(UserId, _dayId);
            CopySources.Clear();
            foreach (var s in sources) CopySources.Add(new CopySourceItemViewModel(s, this));
            HasCopySources = sources.Count > 0;

        }
        catch (Exception)
        {
            Error = "Could not load your other days.";
        }
    }

    internal void ChooseCopy(CopySourceItemViewModel item)
    {
        PendingCopy = item;
        foreach (var s in CopySources) s.IsSelected = s == item;
    }

    [RelayCommand] private void SetAppendMode() => IsReplaceMode = false;
    [RelayCommand] private void SetReplaceMode() => IsReplaceMode = true;

    // An empty day just receives the copy; a day with exercises uses the chosen mode
    [RelayCommand]
    private Task ConfirmCopyAsync() =>
        PendingCopy is { } p ? CopyAsync(p, Items.Count > 0 && IsReplaceMode ? CopyMode.Replace : CopyMode.Append) : Task.CompletedTask;

    [RelayCommand]
    private void CancelCopy()
    {
        PendingCopy = null;
        IsReplaceMode = false;
        IsCopyOpen = false;
    }

    private async Task CopyAsync(CopySourceItemViewModel item, CopyMode mode)
    {
        try
        {
            var r = await _days.CopyAsync(UserId, item.Source.SplitDayId, _dayId, mode);
            if (!r.Success) { Error = r.Error; Info = null; return; }

            Error = null;
            Info = r.Skipped > 0
                ? $"Copied {r.Added} exercise(s), skipped {r.Skipped} already in the list."
                : $"Copied {r.Added} exercise(s).";
            PendingCopy = null;
            IsReplaceMode = false;
            IsCopyOpen = false;
            await ReloadAsync();

        }
        catch (Exception)
        {
            Error = "Could not copy the exercises.";
        }
    }

    [RelayCommand] private void NewExercise() => _shell().ShowCustomExercises(_menuDay!, _splitName);

    [RelayCommand] private void Back() => _shell().ShowWorkouts();
}