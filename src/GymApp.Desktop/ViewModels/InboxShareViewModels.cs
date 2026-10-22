using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Contracts;
using GymApp.Services;

namespace GymApp.Desktop.ViewModels;

/// <summary>"Bench press", "1 warm-up · 3 working", for a shared day's preview.</summary>
public record SharedExerciseLine(int Number, string Name, string Plan)
{
    public static IReadOnlyList<SharedExerciseLine> Of(IReadOnlyList<SharedExercise> list) =>
        list.Select((e, i) => new SharedExerciseLine(i + 1, e.Name,
            e.WarmupSets > 0 ? $"{e.WarmupSets} warm-up · {e.WorkingSets} working" : $"{e.WorkingSets} working")).ToList();

    /// <summary>The first few names as chips, then "+3 more".</summary>
    public static IReadOnlyList<string> Chips(IEnumerable<string> names, int shown = 3)
    {
        var all = names.ToList();
        return all.Count <= shown + 1 ? all : all.Take(shown).Append($"+{all.Count - shown} more").ToList();
    }
}

/// <summary>A shared day in a split's preview: its name and exercises.</summary>
public record SharedDayPreview(string Name, string CountText, IReadOnlyList<SharedExerciseLine> Exercises);

public partial class SharedSplitRowViewModel : InboxRowViewModel
{
    private readonly InboxViewModel _owner;

    public SharedSplitRowViewModel(SharedSplitItem item, DateTime nowLocal, InboxViewModel owner) : base(item, nowLocal)
    {
        _owner = owner;
        Share = item;
        var split = item.Split;
        var exercises = split.Days.Sum(d => d.Exercises.Count);
        Detail = $"Whole split · {split.Days.Count} day{(split.Days.Count == 1 ? "" : "s")} · {exercises} exercise{(exercises == 1 ? "" : "s")}";
        Chips = SharedExerciseLine.Chips(split.Days.Select(d => d.Name), 5);
        Days = split.Days.Select(d => new SharedDayPreview(d.Name,
            d.Exercises.Count == 1 ? "1 exercise" : $"{d.Exercises.Count} exercises", SharedExerciseLine.Of(d.Exercises))).ToList();
    }

    public SharedSplitItem Share { get; }
    public string Title => Share.Split.Name;
    public string Headline => $"{Username} shared their whole split";
    public string Detail { get; }
    public IReadOnlyList<string> Chips { get; }
    public IReadOnlyList<SharedDayPreview> Days { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewLabel))]
    public partial bool IsExpanded { get; set; }

    public string PreviewLabel => IsExpanded ? "Hide" : "Preview";

    [RelayCommand] private void TogglePreview() => IsExpanded = !IsExpanded;
    [RelayCommand] private void Add() => _owner.OpenSplitAdd(this);
    [RelayCommand] private Task Dismiss() => _owner.DismissAsync(Share.ShareId);
}

public partial class SharedWorkoutRowViewModel : InboxRowViewModel
{
    private readonly InboxViewModel _owner;

    public SharedWorkoutRowViewModel(SharedWorkoutItem item, DateTime nowLocal, InboxViewModel owner) : base(item, nowLocal)
    {
        _owner = owner;
        Share = item;
        var w = item.Workout;
        var sets = w.Exercises.Sum(e => e.WarmupSets + e.WorkingSets);
        Detail = $"From {w.SplitName} · {w.Exercises.Count} exercise{(w.Exercises.Count == 1 ? "" : "s")} · {sets} sets";
        Chips = SharedExerciseLine.Chips(w.Exercises.Select(e => e.Name));
        Exercises = SharedExerciseLine.Of(w.Exercises);
    }

    public SharedWorkoutItem Share { get; }
    public string Title => Share.Workout.DayName;
    public string Headline => $"{Username} shared a workout";
    public string Detail { get; }
    public IReadOnlyList<string> Chips { get; }
    public IReadOnlyList<SharedExerciseLine> Exercises { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewLabel))]
    public partial bool IsExpanded { get; set; }

    public string PreviewLabel => IsExpanded ? "Hide" : "Preview";

    [RelayCommand] private void TogglePreview() => IsExpanded = !IsExpanded;
    [RelayCommand] private Task Add() => _owner.OpenWorkoutAddAsync(this);
    [RelayCommand] private Task Dismiss() => _owner.DismissAsync(Share.ShareId);
}

/// <summary>A weekday in the start-day pill.</summary>
public partial class WeekdayOptionViewModel(DayOfWeek day, SplitAddViewModel owner) : ObservableObject
{
    public DayOfWeek Day => day;
    public string Label => day.ToString()[..3];

    [ObservableProperty] public partial bool IsOn { get; set; }

    [RelayCommand] private void Choose() => owner.Start = day;
}

/// <summary>The popup when adding a shared split: which weekday day 1 falls on, and (for a preset everyone has)
/// whether it replaces the exercises on the user's own copy or becomes a new custom split.</summary>
public partial class SplitAddViewModel : ObservableObject
{
    private Func<SharedSplitRowViewModel, DayOfWeek, SplitImportMode, Task<string?>>? _confirm;

    public SplitAddViewModel()
    {
        Weekdays = Enumerable.Range(0, 7).Select(i => new WeekdayOptionViewModel((DayOfWeek)((i + 1) % 7), this)).ToList();
    }

    public IReadOnlyList<WeekdayOptionViewModel> Weekdays { get; }

    [ObservableProperty] public partial bool IsOpen { get; set; }
    [ObservableProperty] public partial SharedSplitRowViewModel? Row { get; set; }
    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial string Intro { get; set; } = "";
    [ObservableProperty] public partial bool CanReplaceMine { get; set; }
    [ObservableProperty] public partial string ReplaceLabel { get; set; } = "";
    [ObservableProperty] public partial string ModeNote { get; set; } = "";
    [ObservableProperty] public partial string WeekText { get; set; } = "";
    [ObservableProperty] public partial string? LeftOutNote { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewCustom), nameof(ModeIndex))]
    public partial bool IsReplaceMine { get; set; }

    public bool IsNewCustom => !IsReplaceMine;
    public int ModeIndex => IsReplaceMine ? 0 : 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartIndex))]
    public partial DayOfWeek Start { get; set; } = DayOfWeek.Monday;

    /// <summary>Monday = 0, for the pill.</summary>
    public int StartIndex => ((int)Start + 6) % 7;

    partial void OnStartChanged(DayOfWeek value) => Refresh();
    partial void OnIsReplaceMineChanged(bool value) => Refresh();

    public void Open(SharedSplitRowViewModel row, DayOfWeek today,
                     Func<SharedSplitRowViewModel, DayOfWeek, SplitImportMode, Task<string?>> confirm)
    {
        _confirm = confirm;
        Row = row;
        var split = row.Share.Split;
        Title = $"Start {split.Name}";
        Intro = $"{row.Username} shared their whole split: all {split.Days.Count} days and the exercises in each, with their planned sets.";
        CanReplaceMine = SplitShareService.CanReplaceMine(split);
        ReplaceLabel = $"Use my {split.Name}";
        Error = null;
        IsReplaceMine = CanReplaceMine;
        Start = today;
        Refresh();
        IsOpen = true;
    }

    private void Refresh()
    {
        foreach (var w in Weekdays) w.IsOn = w.Day == Start;
        if (Row is null) return;
        var split = Row.Share.Split;
        var replace = IsReplaceMine && CanReplaceMine;
        ModeNote = replace
            ? $"The shared exercises replace yours on its {split.Days.Count} days. Your logged sets and PRs stay."
            : "Added as a new custom split, so your other splits keep their exercises.";
        WeekText = string.Join(" · ", SplitShareService.Week(split, Start).Select(w => $"{w.Day.Weekday.ToString()[..3]} {w.Day.Name}"));
        var left = replace ? 0 : SplitShareService.DaysLeftOut(split, Start);
        LeftOutNote = left > 0
            ? $"Custom splits keep fixed weekdays, so {left} of its days don't fit in a week and are left out."
            : null;
    }

    [RelayCommand] private void UseMine() => IsReplaceMine = true;
    [RelayCommand] private void UseNewCustom() => IsReplaceMine = false;
    [RelayCommand] private void Close() => IsOpen = false;

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (Row is null || _confirm is null || IsBusy) return;
        IsBusy = true;
        try
        {
            Error = await _confirm(Row, Start, IsReplaceMine && CanReplaceMine ? SplitImportMode.ReplaceMine : SplitImportMode.NewCustom);
            if (Error is null) IsOpen = false;
        }
        finally { IsBusy = false; }
    }
}

/// <summary>A day of the user's active split to load a shared workout into.</summary>
public partial class TargetDayViewModel(MenuDay day, WorkoutAddViewModel owner) : ObservableObject
{
    public MenuDay Day => day;
    public string Name => day.Name;
    public string Detail => (day.Weekdays.Length > 0 ? day.Weekdays + " · " : "")
                            + (day.ExerciseCount == 1 ? "1 exercise" : $"{day.ExerciseCount} exercises");

    [ObservableProperty] public partial bool IsSelected { get; set; }

    [RelayCommand] private void Choose() => owner.Choose(this);
}

/// <summary>The popup when adding a shared workout: which day of the user's current split gets it, and whether it adds
/// to that day's list or replaces it.</summary>
public partial class WorkoutAddViewModel : ObservableObject
{
    private Func<SharedWorkoutRowViewModel, MenuDay, CopyMode, Task<string?>>? _confirm;

    public ObservableCollection<TargetDayViewModel> Days { get; } = new();

    [ObservableProperty] public partial bool IsOpen { get; set; }
    [ObservableProperty] public partial SharedWorkoutRowViewModel? Row { get; set; }
    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial string Intro { get; set; } = "";
    [ObservableProperty] public partial bool HasSplit { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChosen), nameof(TargetHasExercises), nameof(ModeQuestion))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial TargetDayViewModel? Chosen { get; set; }

    public bool HasChosen => Chosen is not null;
    public bool TargetHasExercises => Chosen?.Day.ExerciseCount > 0;
    public string ModeQuestion => Chosen is null ? "" : $"{Chosen.Name} already has exercises. What should happen?";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAppend))]
    public partial bool IsReplace { get; set; }

    public bool IsAppend => !IsReplace;

    public void Open(SharedWorkoutRowViewModel row, WorkoutMenu? menu,
                     Func<SharedWorkoutRowViewModel, MenuDay, CopyMode, Task<string?>> confirm)
    {
        _confirm = confirm;
        Row = row;
        Title = $"Add {row.Title}";
        Intro = menu is null
            ? "Shared workouts load into a day of your current split. Pick a split first."
            : $"Pick the day of {menu.SplitName} to load {row.Username}'s {row.Share.Workout.Exercises.Count} exercises into.";
        HasSplit = menu is not null;
        Error = null;
        IsReplace = false;
        Days.Clear();
        foreach (var d in menu?.Days ?? []) Days.Add(new TargetDayViewModel(d, this));
        // A day with the same name is the likely match ("Legs" into "Legs")
        Chosen = null;
        if (Days.FirstOrDefault(d => string.Equals(d.Name, row.Title, StringComparison.OrdinalIgnoreCase)) is { } same) Choose(same);
        IsOpen = true;
    }

    internal void Choose(TargetDayViewModel day)
    {
        Chosen = day;
        foreach (var d in Days) d.IsSelected = d == day;
    }

    [RelayCommand] private void SetAppend() => IsReplace = false;
    [RelayCommand] private void SetReplace() => IsReplace = true;
    [RelayCommand] private void Close() => IsOpen = false;

    private bool CanConfirm() => Chosen is not null;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        if (Row is null || Chosen is null || _confirm is null || IsBusy) return;
        IsBusy = true;
        try
        {
            Error = await _confirm(Row, Chosen.Day, TargetHasExercises && IsReplace ? CopyMode.Replace : CopyMode.Append);
            if (Error is null) IsOpen = false;
        }
        finally { IsBusy = false; }
    }
}
