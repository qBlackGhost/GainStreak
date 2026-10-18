using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Core;
using GymApp.Services;

namespace GymApp.Desktop.ViewModels;

/// <summary>One day of the "This week" row.</summary>
public class WeekChipViewModel(WeekChip chip)
{
    public string Day { get; } = chip.Date.ToString("ddd", CultureInfo.InvariantCulture);
    public string Symbol { get; } = chip.Kind switch
    {
        WeekChipKind.Done => "✔",
        WeekChipKind.MadeUp => "↺",
        WeekChipKind.Missed => "✘",
        WeekChipKind.Rest => "–",
        _ => "·"
    };
    public string Tip { get; } = chip.Kind switch
    {
        WeekChipKind.Done => "Done",
        WeekChipKind.MadeUp => "Made up",
        WeekChipKind.Missed => "Missed",
        WeekChipKind.Rest => "Rest day",
        _ => "Upcoming"
    };
    public bool IsToday { get; } = chip.IsToday;
    public bool IsDone { get; } = chip.Kind == WeekChipKind.Done;
    public bool IsMadeUp { get; } = chip.Kind == WeekChipKind.MadeUp;
    public bool IsMissed { get; } = chip.Kind == WeekChipKind.Missed;
}

/// <summary>The "This week" card: Monday to Sunday, today highlighted.</summary>
public partial class WeekCardViewModel : ObservableObject
{
    [ObservableProperty] public partial IReadOnlyList<WeekChipViewModel> Chips { get; set; } = [];

    public void Apply(IReadOnlyList<WeekChip> chips) => Chips = chips.Select(c => new WeekChipViewModel(c)).ToList();
}

/// <summary>One exercise on the "Today's workout" card.</summary>
public class TodayExerciseViewModel(TodayExercise e)
{
    public string Name { get; } = e.Name;
    public string Plan { get; } = e.WarmupSets > 0
        ? $"{e.WorkingSets} × working, {e.WarmupSets} warm-up"
        : $"{e.WorkingSets} × working";
    public string LastTime { get; } = e.LastTopSet is { } s
        ? $"Last time: {NumberParsing.FormatKg(s.WeightKg)} kg × {s.Reps}"
        : "First time";
}

/// <summary>The "Today's workout" card: the planned day and its exercises, or why there's nothing to show.</summary>
public partial class TodayWorkoutCardViewModel(Func<MainViewModel> shell) : ObservableObject
{
    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial string? EmptyText { get; set; }
    [ObservableProperty] public partial IReadOnlyList<TodayExerciseViewModel> Exercises { get; set; } = [];
    [ObservableProperty] public partial bool ShowBuildLink { get; set; }
    [ObservableProperty] public partial bool ShowPickSplit { get; set; }

    public bool HasExercises => Exercises.Count > 0;
    partial void OnExercisesChanged(IReadOnlyList<TodayExerciseViewModel> value) => OnPropertyChanged(nameof(HasExercises));

    public void Apply(TodayWorkoutInfo info)
    {
        Exercises = info.Exercises.Select(e => new TodayExerciseViewModel(e)).ToList();
        Title = !info.HasSplit ? "No split selected" : info.DayName ?? "Rest day";
        ShowPickSplit = !info.HasSplit;
        ShowBuildLink = info.DayName is not null && info.Exercises.Count == 0;
        EmptyText = !info.HasSplit ? "Pick a split to get a weekly schedule."
                  : info.DayName is null ? "Enjoy the rest. You can still log an extra workout."
                  : info.Exercises.Count == 0 ? $"You haven't built a workout for {info.DayName} yet."
                  : null;
    }

    [RelayCommand] private void BuildWorkout() => shell().ShowWorkouts();
    [RelayCommand] private void PickSplit() => shell().ShowSplits();
}

public class RecentPrViewModel(RecentPr pr)
{
    public string Exercise { get; } = pr.ExerciseName;
    public string Value { get; } = $"{NumberParsing.FormatKg(pr.WeightKg)} kg × {pr.Reps}";
    public string Date { get; } = pr.Date.ToString("d MMM", CultureInfo.InvariantCulture);
}

/// <summary>The "Recent PRs" card: the last 3. Hidden when there are none.</summary>
public partial class RecentPrsCardViewModel : ObservableObject
{
    [ObservableProperty] public partial IReadOnlyList<RecentPrViewModel> Items { get; set; } = [];

    public bool HasAny => Items.Count > 0;
    partial void OnItemsChanged(IReadOnlyList<RecentPrViewModel> value) => OnPropertyChanged(nameof(HasAny));

    public void Apply(IReadOnlyList<RecentPr> prs) => Items = prs.Select(p => new RecentPrViewModel(p)).ToList();
}
