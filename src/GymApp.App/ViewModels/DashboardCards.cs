using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Core;
using GymApp.Services;

namespace GymApp.App.ViewModels;

/// <summary>One day of the "This week" row: the weekday, the planned day's name and how it went.</summary>
public class WeekChipViewModel(WeekChip chip, string name)
{
    public string Day { get; } = chip.Date.ToString("ddd", CultureInfo.InvariantCulture);
    public string Name { get; } = name;
    public string Tip { get; } = chip.Kind switch
    {
        WeekChipKind.Done => "Done",
        WeekChipKind.MadeUp => "Made up",
        WeekChipKind.Missed => "Missed",
        WeekChipKind.Rest => "Rest day",
        WeekChipKind.Vacation => "Vacation",
        _ => "Upcoming"
    };

    // Today has its own look (open or done), so the other kinds only describe the other days
    public bool IsTodayDone { get; } = chip.IsToday && chip.Kind == WeekChipKind.Done;
    public bool IsTodayOpen { get; } = chip.IsToday && chip.Kind is not (WeekChipKind.Done or WeekChipKind.Vacation);
    public bool IsToday { get; } = chip.IsToday;
    /// <summary>A vacation day (0.19.0), today included: neither done nor missed.</summary>
    public bool IsVacation { get; } = chip.Kind == WeekChipKind.Vacation;
    public bool IsDone { get; } = !chip.IsToday && chip.Kind == WeekChipKind.Done;
    public bool IsMadeUp { get; } = !chip.IsToday && chip.Kind == WeekChipKind.MadeUp;
    public bool IsMissed { get; } = !chip.IsToday && chip.Kind == WeekChipKind.Missed;
    public bool IsRest { get; } = !chip.IsToday && chip.Kind == WeekChipKind.Rest;
    public bool IsUpcoming { get; } = !chip.IsToday && chip.Kind == WeekChipKind.Upcoming;
}

/// <summary>The "This week" card: Monday to Sunday, today highlighted.</summary>
public partial class WeekCardViewModel : ObservableObject
{
    [ObservableProperty] public partial IReadOnlyList<WeekChipViewModel> Chips { get; set; } = [];

    /// <summary><paramref name="names"/>: the planned day's name per date ("Push", "Rest"); a missing date shows no name.</summary>
    /// <summary>The week has a vacation day (0.19.0): the legend explains the moon.</summary>
    [ObservableProperty] public partial bool HasVacation { get; set; }

    public void Apply(IReadOnlyList<WeekChip> chips, IReadOnlyDictionary<DateOnly, string> names)
    {
        Chips = chips.Select(c => new WeekChipViewModel(c, names.GetValueOrDefault(c.Date, ""))).ToList();
        HasVacation = chips.Any(c => c.Kind == WeekChipKind.Vacation);
    }
}

/// <summary>One exercise on the "Today's workout" card.</summary>
public class TodayExerciseViewModel(TodayExercise e)
{
    public string Name { get; } = e.Name;
    public string Plan { get; } = e.WarmupSets > 0
        ? $"{e.WorkingSets} × working, {e.WarmupSets} warm-up"
        : $"{e.WorkingSets} × working";
    /// <summary>"1 + 3": warm-up + working sets.</summary>
    public string Sets { get; } = $"{e.WarmupSets} + {e.WorkingSets}";
    public string LastTime { get; } = e.LastTopSet is { } s
        ? $"{NumberParsing.FormatKg(s.WeightKg)} kg × {s.Reps}"
        : "First time";
}

/// <summary>The "Today's workout" card: the planned day and its exercises, or why there's nothing to show.</summary>
/// <param name="canVerify">Phones, where a workout can be verified at a gym, so a rest day's workout adds a day.</param>
public partial class TodayWorkoutCardViewModel(Func<MainViewModel> shell, bool canVerify = false) : ObservableObject
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
                  : info.DayName is null ? (canVerify ? "Enjoy the rest. A gym workout adds a day." : "Enjoy the rest.")
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

/// <summary>One row of the dashboard's friends board.</summary>
public class BoardRowViewModel(LeaderboardRow row)
{
    public string Rank { get; } = row.Rank.ToString(CultureInfo.InvariantCulture);
    public string Name { get; } = row.IsYou ? "You" : row.Username;
    public string Streak { get; } = row.CurrentStreak.ToString(CultureInfo.InvariantCulture);
    public string Level { get; } = $"Level {row.Level.Number} · {row.Level.Name}";
    public string Points { get; } = row.Points.ToString("N0", CultureInfo.InvariantCulture);
    public bool IsYou { get; } = row.IsYou;
}
