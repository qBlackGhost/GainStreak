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

namespace GymApp.Desktop.ViewModels;

/// <summary>One kg/reps row in the check-in popup. Text, so half-typed values never break anything.</summary>
public partial class CheckInSetRowViewModel : ObservableObject
{
    private readonly CheckInViewModel _owner;

    public CheckInSetRowViewModel(CheckInViewModel owner, SetKind kind, int number, string weightText, string repsText)
    {
        _owner = owner;
        Kind = kind;
        Label = kind == SetKind.Warmup ? $"Warm-up {number}" : $"Set {number}";
        WeightText = weightText;
        RepsText = repsText;
    }

    public SetKind Kind { get; }
    public string Label { get; }

    [ObservableProperty] public partial string WeightText { get; set; } = "";
    [ObservableProperty] public partial string RepsText { get; set; } = "";

    partial void OnWeightTextChanged(string value) => _owner.RowChanged();
    partial void OnRepsTextChanged(string value) => _owner.RowChanged();

    public bool IsEmpty => string.IsNullOrWhiteSpace(WeightText) && string.IsNullOrWhiteSpace(RepsText);

    /// <summary>The set, when both kg and reps are valid.</summary>
    public SetInput? ToInput() =>
        NumberParsing.TryParseKg(WeightText, out var kg) && NumberParsing.TryParseReps(RepsText, out var reps)
            ? new SetInput(Kind, reps, kg)
            : null;
}

/// <summary>One exercise in the check-in popup, with a row per planned set.</summary>
public partial class CheckInExerciseViewModel : ObservableObject
{
    private readonly CheckInViewModel _owner;

    public CheckInExerciseViewModel(CheckInViewModel owner, CheckInExerciseInfo info)
    {
        _owner = owner;
        ExerciseId = info.ExerciseId;
        Name = info.Name;
        WorkingSets = info.WorkingSets;
        PlanSummary = info.WarmupSets > 0
            ? $"{info.WarmupSets} warm-up · {info.WorkingSets} working"
            : $"{info.WorkingSets} working";
        Fill(info.TodaySets.Count > 0 ? info.TodaySets : null, info.WarmupSets, info.WorkingSets);
    }

    public int ExerciseId { get; }
    public string Name { get; }
    public int WorkingSets { get; }
    public string PlanSummary { get; }
    public ObservableCollection<CheckInSetRowViewModel> Rows { get; } = new();

    [ObservableProperty] public partial bool IsFilled { get; set; }
    [ObservableProperty] public partial string? Info { get; set; }

    /// <summary>Rows from <paramref name="sets"/> when given (today's or last time's), padded to the plan;
    /// otherwise empty rows following the plan.</summary>
    internal void Fill(IReadOnlyList<SetEntry>? sets, int warmupSets, int workingSets)
    {
        var warmups = sets?.Where(s => s.Kind == SetKind.Warmup).ToList() ?? [];
        var working = sets?.Where(s => s.Kind == SetKind.Working).ToList() ?? [];

        Rows.Clear();
        for (var i = 0; i < Math.Max(warmupSets, warmups.Count); i++)
            Rows.Add(Row(SetKind.Warmup, i, warmups));
        for (var i = 0; i < Math.Max(workingSets, working.Count); i++)
            Rows.Add(Row(SetKind.Working, i, working));
    }

    private CheckInSetRowViewModel Row(SetKind kind, int i, List<SetEntry> sets) =>
        i < sets.Count
            ? new(_owner, kind, i + 1, NumberParsing.FormatKg(sets[i].WeightKg), sets[i].Reps.ToString(CultureInfo.InvariantCulture))
            : new(_owner, kind, i + 1, "", "");

    /// <summary>The fully filled rows, as the rule sees them.</summary>
    internal CheckInExercise ToRuleInput(DateOnly today) =>
        new(ExerciseId, WorkingSets, Rows.Select(r => r.ToInput()).OfType<SetInput>()
            .Select((s, i) => new SetEntry(today, i + 1, s.Kind, s.Reps, s.WeightKg)).ToList());

    [RelayCommand] private Task SameAsLastTimeAsync() => _owner.PrefillAsync(this);
}

/// <summary>
/// The "I went today" popup: kg and reps for today's exercises, then the check-in. The 50% rule itself lives in
/// <see cref="CheckInRule"/> (shown live here) and is enforced again by <see cref="WorkoutService.LogTodayAsync"/>.
/// </summary>
public partial class CheckInViewModel : ViewModelBase
{
    private readonly WorkoutService _workouts;
    private readonly SetService _sets;
    private readonly WorkoutMenuService _menu;
    private readonly Func<MainViewModel> _shell;
    private readonly int _userId;
    private readonly Action<LogResult> _logged;
    private readonly Action _closed;
    private DateOnly _today;
    private bool _loading;

    public CheckInViewModel(WorkoutService workouts, SetService sets, WorkoutMenuService menu, Func<MainViewModel> shell,
                            int userId, Action<LogResult> logged, Action closed)
    {
        _workouts = workouts;
        _sets = sets;
        _menu = menu;
        _shell = shell;
        _userId = userId;
        _logged = logged;
        _closed = closed;
        _ = LoadAsync(null);
    }

    public ObservableCollection<CheckInExerciseViewModel> Exercises { get; } = new();
    public ObservableCollection<CheckInDay> PickableDays { get; } = new();

    [ObservableProperty] public partial string Title { get; set; } = "Check in";
    [ObservableProperty] public partial bool IsLoaded { get; set; }
    [ObservableProperty] public partial bool HasExercises { get; set; }
    [ObservableProperty] public partial bool ShowDayPicker { get; set; }
    [ObservableProperty] public partial CheckInDay? PickedDay { get; set; }
    [ObservableProperty] public partial string? EmptyText { get; set; }
    [ObservableProperty] public partial bool CanOpenWorkouts { get; set; }
    [ObservableProperty] public partial bool CanOpenSplits { get; set; }
    [ObservableProperty] public partial string CounterText { get; set; } = "";
    [ObservableProperty] public partial bool CanLog { get; set; }
    [ObservableProperty] public partial bool IsSaving { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }

    private CheckInDay? _day;

    private async Task LoadAsync(int? splitDayId)
    {
        _loading = true;
        try
        {
            var info = await _workouts.GetCheckInAsync(_userId, splitDayId);
            _today = info.Date;
            _day = info.Day;

            PickableDays.Clear();
            foreach (var d in info.PickableDays) PickableDays.Add(d);
            ShowDayPicker = info.PlannedDay is null && PickableDays.Count > 0;
            PickedDay = ShowDayPicker ? PickableDays.FirstOrDefault(d => d.SplitDayId == info.Day?.SplitDayId) : null;

            Title = info.PlannedDay is { } planned ? $"{planned.Name} day" : "Extra workout";
            Exercises.Clear();
            foreach (var e in info.Exercises) Exercises.Add(new CheckInExerciseViewModel(this, e));
            HasExercises = Exercises.Count > 0;

            // Nothing to fill in: point to where the workout gets built
            CanOpenSplits = !info.HasSplit;
            CanOpenWorkouts = info.HasSplit && !HasExercises;
            EmptyText = HasExercises ? null
                : !info.HasSplit ? "You haven't picked a split yet. Pick one, then build your workouts."
                : info.Day is { } day ? $"You haven't built a workout for {day.Name} yet."
                : "You haven't built any workouts yet.";

            Error = null;
            IsLoaded = true;
        }
        catch (Exception)
        {
            Error = "Could not load today's workout.";
        }
        finally
        {
            _loading = false;
        }
        Refresh();
    }

    // Rest day: switching the day reloads its exercises (anything typed for the old day is dropped)
    partial void OnPickedDayChanged(CheckInDay? value)
    {
        if (_loading || value is null || value.SplitDayId == _day?.SplitDayId) return;
        _ = LoadAsync(value.SplitDayId);
    }

    internal void RowChanged()
    {
        if (_loading) return;
        Error = null;
        Refresh();
    }

    private void Refresh()
    {
        foreach (var e in Exercises) e.IsFilled = CheckInRule.IsFilled(e.ToRuleInput(_today));
        var status = CheckInRule.Evaluate(Exercises.Select(e => e.ToRuleInput(_today)));
        CounterText = status.Total == 0 ? ""
            : $"{status.Filled} of {status.Total} exercise{(status.Total == 1 ? "" : "s")} filled · need {status.Required}";
        CanLog = status.CanLog && !IsSaving;
    }

    internal async Task PrefillAsync(CheckInExerciseViewModel exercise)
    {
        try
        {
            var last = await _sets.GetLastSessionAsync(_userId, exercise.ExerciseId, _today);
            if (last.Count == 0) { exercise.Info = "No earlier session to copy yet."; return; }

            _loading = true;
            var plannedWarmups = exercise.Rows.Count(r => r.Kind == SetKind.Warmup);
            exercise.Fill(last, plannedWarmups, exercise.WorkingSets);
            _loading = false;
            exercise.Info = $"Filled from {ExerciseViewModel.FormatDate(last[0].Date)}.";
            Refresh();
        }
        catch (Exception)
        {
            _loading = false;
            Error = "Could not load your last session.";
        }
    }

    [RelayCommand]
    private async Task LogAsync()
    {
        if (!CanLog) return;

        // Check every row first, so nothing is saved when one is half filled
        var inputs = new List<(CheckInExerciseViewModel Exercise, List<SetInput> Sets)>();
        foreach (var e in Exercises)
        {
            var sets = new List<SetInput>();
            foreach (var row in e.Rows)
            {
                if (row.IsEmpty) continue;   // empty rows are skipped
                if (row.ToInput() is not { } set)
                {
                    Error = $"{e.Name}, {row.Label}: enter the weight (0 for bodyweight) and the number of reps.";
                    return;
                }
                sets.Add(set);
            }
            inputs.Add((e, sets));
        }

        IsSaving = true;
        CanLog = false;
        try
        {
            // Sets are saved even when the check-in fails: they're real training data
            foreach (var (e, sets) in inputs)
            {
                var saved = await _sets.SaveDayAsync(_userId, e.ExerciseId, sets);
                if (!saved.Success) { Error = $"{e.Name}: {saved.Error}"; return; }
            }

            var result = await _workouts.LogTodayAsync(_userId, _day?.SplitDayId);
            if (!result.Success) { Error = result.Error; return; }
            _logged(result);
        }
        catch (Exception)
        {
            Error = "Could not save your workout.";
        }
        finally
        {
            IsSaving = false;
            Refresh();
        }
    }

    [RelayCommand] private void Cancel() => _closed();

    [RelayCommand]
    private async Task OpenWorkoutsAsync()
    {
        try
        {
            var menu = await _menu.GetAsync(_userId);
            var day = menu?.Days.FirstOrDefault(d => d.SplitDayId == _day?.SplitDayId);
            if (menu is not null && day is not null) _shell().ShowDay(day, menu.SplitName);
            else _shell().ShowWorkouts();
        }
        catch (Exception)
        {
            _shell().ShowWorkouts();
        }
    }

    [RelayCommand] private void OpenSplits() => _shell().ShowSplits();
}
