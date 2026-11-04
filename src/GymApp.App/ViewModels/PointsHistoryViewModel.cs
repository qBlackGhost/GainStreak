using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Core;
using GymApp.Services;

namespace GymApp.App.ViewModels;

/// <summary>One ledger line. The kind flags pick the row's icon tile; an adjustment can be negative.</summary>
public record PointsRow(string ReasonText, string DateText, string AmountText,
    bool IsWorkout, bool IsWeek, bool IsCalories, bool IsAdjustment, bool IsNegative);

/// <summary>A Monday-to-Sunday week of the ledger with its subtotal, e.g. "This week" · "+20".</summary>
public record PointsWeek(string Label, string SumText, IReadOnlyList<PointsRow> Rows);

public enum PointsFilter { All, Workouts, Weeks, Calories }

/// <summary>The points history popup over the dashboard: total, level progress, and the ledger by week, newest first,
/// filtered by kind. <see cref="Open"/> reloads it each time it is shown.</summary>
public partial class PointsHistoryViewModel : ViewModelBase
{
    private readonly PointsService _points;
    private readonly SessionService _session;
    private IReadOnlyList<PointsLine> _history = [];
    private Action _closed = () => { };

    public PointsHistoryViewModel(PointsService points, SessionService session)
    {
        _points = points;
        _session = session;
    }

    /// <summary>Raised when the popup is shown, so the view can move focus into it (Escape then closes it).</summary>
    public event Action? Opened;

    public ObservableCollection<PointsWeek> Weeks { get; } = new();

    [ObservableProperty] public partial string TotalText { get; set; } = "0";
    [ObservableProperty] public partial string LevelText { get; set; } = "";
    [ObservableProperty] public partial int LevelNumber { get; set; } = 1;
    [ObservableProperty] public partial double LevelProgress { get; set; }
    [ObservableProperty] public partial string ToNextAmount { get; set; } = "";
    [ObservableProperty] public partial string ToNextText { get; set; } = "";
    [ObservableProperty] public partial bool IsEmpty { get; set; }
    [ObservableProperty] public partial string EmptyText { get; set; } = "";
    [ObservableProperty] public partial string? Error { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterIndex), nameof(IsAll), nameof(IsWorkouts), nameof(IsWeeks), nameof(IsCalories))]
    public partial PointsFilter Filter { get; set; }

    public int FilterIndex => (int)Filter;
    public bool IsAll => Filter == PointsFilter.All;
    public bool IsWorkouts => Filter == PointsFilter.Workouts;
    public bool IsWeeks => Filter == PointsFilter.Weeks;
    public bool IsCalories => Filter == PointsFilter.Calories;

    // The rules, for the footer
    public string WorkoutPoints { get; } = $"+{PointsCalculator.WorkoutPoints}";
    public string ReducedWorkoutPoints { get; } = $"+{PointsCalculator.ReducedWorkoutPoints}";
    public string WeekPoints { get; } = $"+{PointsCalculator.WeekCompletePoints}";
    public string WeekGracePoints { get; } = $"+{PointsCalculator.WeekCompleteGracePoints}";
    public string CaloriePoints { get; } = $"+{PointsCalculator.CalorieTargetPoints}";
    public string CardioPoints { get; } = $"+1 / {PointsCalculator.CardioMinutesPerPoint} min (max +{PointsCalculator.MaxCardioPoints})";

    /// <summary>Shows the popup from the top (All) and loads the ledger; the task ends when it is loaded.</summary>
    public Task Open(Action closed)
    {
        _closed = closed;
        Filter = PointsFilter.All;
        Opened?.Invoke();
        return LoadAsync();
    }

    private async Task LoadAsync()
    {
        Error = null;
        if (_session.CurrentUser is not { } user) return;
        try
        {
            var o = await _points.GetAsync(user.Id);
            if (o is null) return;

            TotalText = N(o.Total);
            LevelNumber = o.Level.Number;
            LevelText = $"Level {o.Level.Number} · {o.Level.Name}";
            LevelProgress = o.Level.Progress;
            if (o.Level.PointsToNext(o.Total) is { } left)
            {
                ToNextAmount = N(left);
                ToNextText = $" to {o.Level.NextName}";
            }
            else
            {
                ToNextAmount = "";
                ToNextText = "Max level";
            }
            _history = o.History;
            Rebuild();
        }
        catch (Exception)
        {
            Error = "Could not load your points.";
        }
    }

    partial void OnFilterChanged(PointsFilter value) => Rebuild();

    private void Rebuild()
    {
        var thisMonday = PointsCalculator.MondayOf(AppClock.Today);
        Weeks.Clear();
        foreach (var week in _history.Where(Matches)
                     .OrderByDescending(l => l.Date)
                     .GroupBy(l => PointsCalculator.MondayOf(l.Date)))
        {
            var rows = week.Select(ToRow).ToList();
            Weeks.Add(new PointsWeek(WeekLabel(week.Key, thisMonday), Signed(week.Sum(l => l.Amount)), rows));
        }
        IsEmpty = Weeks.Count == 0;
        EmptyText = Filter switch
        {
            PointsFilter.Workouts => $"Log a workout to earn up to {PointsCalculator.WorkoutPoints} points.",
            PointsFilter.Weeks => $"Train every planned day of a week to earn {PointsCalculator.WeekCompletePoints} points.",
            PointsFilter.Calories => $"Log what you ate and land in your target range to earn {PointsCalculator.CalorieTargetPoints} points.",
            _ => "Log a workout or hit your calorie target to earn some."
        };
    }

    private bool Matches(PointsLine l) => Filter switch
    {
        PointsFilter.Workouts => l.Reason is PointsReason.Workout or PointsReason.Cardio,
        PointsFilter.Weeks => l.Reason is PointsReason.WeekComplete or PointsReason.WeekCompleteGrace,
        PointsFilter.Calories => l.Reason == PointsReason.CalorieTarget,
        _ => true
    };

    private static PointsRow ToRow(PointsLine l) => new(
        l.Label,
        l.Date.ToString("ddd d MMM", CultureInfo.InvariantCulture),
        Signed(l.Amount),
        l.Reason is PointsReason.Workout or PointsReason.Cardio,
        l.Reason is PointsReason.WeekComplete or PointsReason.WeekCompleteGrace,
        l.Reason == PointsReason.CalorieTarget,
        l.Reason == PointsReason.Adjustment,
        l.Amount < 0);

    /// <summary>"This week", "Last week", then "12 – 18 Oct" or "28 Sep – 4 Oct" across a month end.</summary>
    private static string WeekLabel(DateOnly monday, DateOnly thisMonday)
    {
        if (monday == thisMonday) return "This week";
        if (monday == thisMonday.AddDays(-7)) return "Last week";
        var sunday = monday.AddDays(6);
        var start = monday.Month == sunday.Month
            ? monday.Day.ToString(CultureInfo.InvariantCulture)
            : monday.ToString("d MMM", CultureInfo.InvariantCulture);
        return $"{start} – {sunday.ToString("d MMM", CultureInfo.InvariantCulture)}";
    }

    private static string Signed(int n) => n < 0 ? $"−{N(-n)}" : $"+{N(n)}";
    private static string N(int n) => n.ToString("N0", CultureInfo.InvariantCulture);

    [RelayCommand] private void ShowAll() => Filter = PointsFilter.All;
    [RelayCommand] private void ShowWorkouts() => Filter = PointsFilter.Workouts;
    [RelayCommand] private void ShowWeeks() => Filter = PointsFilter.Weeks;
    [RelayCommand] private void ShowCalories() => Filter = PointsFilter.Calories;

    [RelayCommand] private void Close() => _closed();
}
