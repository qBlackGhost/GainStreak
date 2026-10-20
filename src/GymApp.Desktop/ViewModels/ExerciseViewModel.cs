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

/// <summary>One row of today's sets. Weight and reps are text so half-typed values never break anything.</summary>
public partial class SetRowViewModel : ObservableObject
{
    private readonly ExerciseViewModel _owner;

    public SetRowViewModel(ExerciseViewModel owner, int number, SetKind kind, string weightText, string repsText)
    {
        _owner = owner;
        Number = number;
        IsWarmup = kind == SetKind.Warmup;
        WeightText = weightText;
        RepsText = repsText;
    }

    [ObservableProperty] public partial int Number { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KindLabel), nameof(KindIndex), nameof(IsWorking))]
    public partial bool IsWarmup { get; set; }

    /// <summary>For the Warm-up / Working pill: 0 = warm-up, 1 = working.</summary>
    public int KindIndex => IsWarmup ? 0 : 1;
    public bool IsWorking => !IsWarmup;

    // Last session's matching set (same kind, same order), shown as placeholders
    [ObservableProperty] public partial string WeightPlaceholder { get; set; } = "kg";
    [ObservableProperty] public partial string RepsPlaceholder { get; set; } = "reps";

    [ObservableProperty] public partial string WeightText { get; set; } = "";
    [ObservableProperty] public partial string RepsText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPrompt))]
    public partial string Prompt { get; set; } = "";

    [ObservableProperty] public partial bool IsNewPr { get; set; }
    [ObservableProperty] public partial bool IsTie { get; set; }

    public string KindLabel => IsWarmup ? "Warm-up" : "Working";
    public bool HasPrompt => Prompt.Length > 0;

    partial void OnIsWarmupChanged(bool value) => _owner.RowChanged();
    partial void OnWeightTextChanged(string value) => _owner.RowChanged();
    partial void OnRepsTextChanged(string value) => _owner.RowChanged();

    public DraftSet ToDraft() => new(
        IsWarmup ? SetKind.Warmup : SetKind.Working,
        NumberParsing.TryParseKg(WeightText, out var kg) ? kg : (double?)null,
        NumberParsing.TryParseReps(RepsText, out var reps) ? reps : (int?)null);

    public void ApplyPrompt(PrPrompt? prompt)
    {
        Prompt = prompt?.Text ?? "";
        IsNewPr = prompt?.Status == PrStatus.NewPr;
        IsTie = prompt?.Status == PrStatus.Tie;
    }

    [RelayCommand] private void ToggleKind() => IsWarmup = !IsWarmup;
    [RelayCommand] private void SetWarmup() => IsWarmup = true;
    [RelayCommand] private void SetWorking() => IsWarmup = false;
    [RelayCommand] private void Remove() => _owner.RemoveRow(this);
}

/// <summary>One line of the PR history. Manual entries can be undone.</summary>
public partial class PrRowViewModel : ObservableObject
{
    private readonly ExerciseViewModel _owner;

    public PrRowViewModel(PrEntry entry, ExerciseViewModel owner)
    {
        _owner = owner;
        Date = entry.Date;
        DateText = ExerciseViewModel.FormatDate(entry.Date);
        BestText = $"{NumberParsing.FormatKg(entry.WeightKg)} kg × {entry.Reps}";
        OneRmText = $"≈ {NumberParsing.FormatKg(Math.Round(entry.OneRepMaxKg, 1))} kg";
        KindText = entry.Kind switch
        {
            PrEntryKind.Baseline => "First",
            PrEntryKind.Manual => "Edited",
            _ => "PR"
        };
        IsManual = entry.Kind == PrEntryKind.Manual;
    }

    public DateOnly Date { get; }
    public string DateText { get; }
    public string BestText { get; }
    public string OneRmText { get; }
    public string KindText { get; }
    public bool IsManual { get; }

    [RelayCommand] private Task Undo() => _owner.RemoveAdjustmentAsync(Date);
}

/// <summary>One set of the "Last time" card.</summary>
public record LastSetRow(string Number, string Kind, string Set, bool IsWarmup);

/// <summary>One bar of "Top set per session": the heaviest working set that day. Height is in chart pixels.</summary>
public record TopSetBar(string Date, string Weight, double Height, bool IsPr);

public partial class ExerciseViewModel : ViewModelBase
{
    /// <summary>The "Top set per session" chart's bar area, in pixels, and how many sessions it shows.</summary>
    public const double ChartHeight = 100;
    private const int ChartSessions = 6;
    private record RowData(SetKind Kind, string Weight, string Reps);

    private readonly SetService _sets;
    private readonly DayExerciseService _days;
    private readonly SessionService _session;
    private readonly Func<MainViewModel> _shell;

    private MenuDay _day = null!;
    private string _splitName = "";
    private int _exerciseId;
    private DateOnly _today;
    private ExerciseHistory _history = new([], []);
    private bool _loading;

    public ObservableCollection<SetRowViewModel> Rows { get; } = new();
    public ObservableCollection<PrRowViewModel> PrEntries { get; } = new();

    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial string Subtitle { get; set; } = "";
    [ObservableProperty] public partial string MuscleText { get; set; } = "";
    [ObservableProperty] public partial string DayText { get; set; } = "";
    [ObservableProperty] public partial string PositionText { get; set; } = "";
    public string DateText => DateTime.Now.ToString("dddd, d MMMM", CultureInfo.InvariantCulture);
    public string BackLabel => _day?.Name ?? "Back";

    // PR card
    [ObservableProperty] public partial bool HasPr { get; set; }
    [ObservableProperty] public partial string PrWeight { get; set; } = "";
    [ObservableProperty] public partial string PrReps { get; set; } = "";
    [ObservableProperty] public partial string PrOneRm { get; set; } = "";
    [ObservableProperty] public partial string PrDateText { get; set; } = "";

    /// <summary>A save just set a new PR: the card glows until the next change.</summary>
    [ObservableProperty] public partial bool IsPrFresh { get; set; }

    // Last time and the top-set chart
    [ObservableProperty] public partial bool HasLast { get; set; }
    [ObservableProperty] public partial string LastDateText { get; set; } = "";
    [ObservableProperty] public partial IReadOnlyList<LastSetRow> LastSets { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<TopSetBar> TopSets { get; set; } = [];
    [ObservableProperty] public partial bool HasTopSets { get; set; }

    private IReadOnlyList<SetEntry> _last = [];
    // The counters always show today's rows by kind; editing the rows (or the counters) also saves them as the plan
    [ObservableProperty] public partial int WarmupSets { get; set; }
    [ObservableProperty] public partial int WorkingSets { get; set; } = 3;
    private int _planWarmup, _planWorking;   // what the day's plan holds in the database

    [ObservableProperty] public partial string BestText { get; set; } = "No PR yet";
    [ObservableProperty] public partial string OneRmText { get; set; } = "";
    [ObservableProperty] public partial string BestNote { get; set; } = "";
    [ObservableProperty] public partial bool HasPrEntries { get; set; }

    [ObservableProperty] public partial bool IsEditingPr { get; set; }
    [ObservableProperty] public partial string PrWeightText { get; set; } = "";
    [ObservableProperty] public partial string PrRepsText { get; set; } = "";
    [ObservableProperty] public partial string PrOneRmInput { get; set; } = "";
    [ObservableProperty] public partial string? PrWeightError { get; set; }
    [ObservableProperty] public partial string? PrRepsError { get; set; }
    [ObservableProperty] public partial string? PrOneRmError { get; set; }

    private void ClearPrErrors() => PrWeightError = PrRepsError = PrOneRmError = null;

    [ObservableProperty] public partial string? Error { get; set; }
    [ObservableProperty] public partial string? Info { get; set; }

    private int UserId => _session.CurrentUser!.Id;

    public ExerciseViewModel(SetService sets, DayExerciseService days, SessionService session, Func<MainViewModel> shell)
    {
        _sets = sets; _days = days; _session = session; _shell = shell;
    }

    internal static string FormatDate(DateOnly date) =>
        date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>Call right after resolving from DI.</summary>
    public void Load(MenuDay day, string splitName, int exerciseId)
    {
        _day = day;
        _splitName = splitName;
        _exerciseId = exerciseId;
        _today = AppClock.Today;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var list = await _days.GetListAsync(UserId, _day.SplitDayId);
            var row = list.FirstOrDefault(x => x.ExerciseId == _exerciseId);
            if (row is null) { Error = "That exercise is not in this day anymore."; return; }

            Title = row.Exercise.Name;
            Subtitle = $"{row.Exercise.MuscleGroup} · {_day.Name}";
            MuscleText = row.Exercise.OwnerUserId is null ? row.Exercise.MuscleGroup.ToString() : "Custom";
            DayText = _day.Weekdays.Length > 0 ? $"{_day.Name} · {_day.Weekdays}" : _day.Name;
            PositionText = $"Exercise {list.IndexOf(row) + 1} of {list.Count}";
            OnPropertyChanged(nameof(BackLabel));
            WarmupSets = _planWarmup = row.WarmupSets;
            WorkingSets = _planWorking = row.WorkingSets;

            _history = await _sets.GetHistoryAsync(UserId, _exerciseId);
            await LoadLastAsync();
            await RebuildFromSavedAsync();
            ShowPr();
        }
        catch (Exception)
        {
            Error = "Could not load this exercise.";
        }
    }

    /// <summary>The last session before today: the "Last time" card and the rows' placeholders.</summary>
    private async Task LoadLastAsync()
    {
        _last = await _sets.GetLastSessionAsync(UserId, _exerciseId, _today);
        HasLast = _last.Count > 0;
        LastDateText = HasLast ? _last[0].Date.ToString("ddd d MMM", CultureInfo.InvariantCulture) : "";
        LastSets = _last.Select((x, i) => new LastSetRow((i + 1).ToString(CultureInfo.InvariantCulture),
            x.Kind == SetKind.Warmup ? "Warm-up" : "Working",
            $"{NumberParsing.FormatKg(x.WeightKg)} kg × {x.Reps}", x.Kind == SetKind.Warmup)).ToList();
    }

    /// <summary>Each row's placeholders are the last session's set of the same kind and position.</summary>
    private void ApplyPlaceholders()
    {
        int warm = 0, work = 0;
        var lastWarm = _last.Where(x => x.Kind == SetKind.Warmup).ToList();
        var lastWork = _last.Where(x => x.Kind == SetKind.Working).ToList();
        foreach (var r in Rows)
        {
            var match = r.IsWarmup ? lastWarm.ElementAtOrDefault(warm++) : lastWork.ElementAtOrDefault(work++);
            r.WeightPlaceholder = match is null ? "kg" : NumberParsing.FormatKg(match.WeightKg);
            r.RepsPlaceholder = match is null ? "reps" : match.Reps.ToString(CultureInfo.InvariantCulture);
        }
    }

    // ---------- today's sets ----------

    private IEnumerable<RowData> PlanRows() =>
        Enumerable.Repeat(new RowData(SetKind.Warmup, "", ""), WarmupSets)
            .Concat(Enumerable.Repeat(new RowData(SetKind.Working, "", ""), WorkingSets));

    // Sets saved today, or empty rows following the plan when nothing is saved yet
    private async Task RebuildFromSavedAsync()
    {
        try
        {
            var saved = await _sets.GetDayAsync(UserId, _exerciseId, _today);
            RebuildRows(saved.Count > 0
                ? saved.Select(s => new RowData(s.Kind, NumberParsing.FormatKg(s.WeightKg), s.Reps.ToString(CultureInfo.InvariantCulture)))
                : PlanRows());

        }
        catch (Exception)
        {
            Error = "Could not load today's sets.";
        }
    }

    private void RebuildRows(IEnumerable<RowData> data)
    {
        _loading = true;
        Rows.Clear();
        var n = 0;
        foreach (var d in data) Rows.Add(new SetRowViewModel(this, ++n, d.Kind, d.Weight, d.Reps));
        _loading = false;
        RowsReshaped();   // today's saved sets set the plan too, so the day list says what the counters say
        RefreshPrompts();
    }

    internal void RowChanged()
    {
        if (_loading) return;
        Info = null;
        IsPrFresh = false;
        RowsReshaped();
        RefreshPrompts();
    }

    private void CountRows()
    {
        WarmupSets = Rows.Count(r => r.IsWarmup);
        WorkingSets = Rows.Count - WarmupSets;
    }

    /// <summary>After you add, remove or switch a row: update the counters and save them as the plan.</summary>
    private void RowsReshaped()
    {
        CountRows();
        _planSave = SavePlanAsync();
    }

    private Task _planSave = Task.CompletedTask;

    // The plan keeps the service's limits (at least one working set); the rows themselves can go beyond them
    private async Task SavePlanAsync()
    {
        var warmup = Math.Clamp(WarmupSets, 0, DayExerciseService.MaxWarmupSets);
        var working = Math.Clamp(WorkingSets, 1, DayExerciseService.MaxWorkingSets);
        if (warmup == _planWarmup && working == _planWorking) return;
        _planWarmup = warmup;
        _planWorking = working;
        try
        {
            var r = await _days.SetPlannedSetsAsync(UserId, _day.SplitDayId, _exerciseId, warmup, working);
            if (!r.Success) Error = r.Error;
        }
        catch (Exception)
        {
            Error = "Could not save the planned sets.";
        }
    }

    private void RefreshPrompts()
    {
        ApplyPlaceholders();
        var draft = Rows.Select(r => r.ToDraft()).ToList();
        for (var i = 0; i < Rows.Count; i++)
            Rows[i].ApplyPrompt(PrPrompts.ForRow(draft, i, _history.Sets, _history.Adjustments, _today));
    }

    internal void RemoveRow(SetRowViewModel row)
    {
        Rows.Remove(row);
        for (var i = 0; i < Rows.Count; i++) Rows[i].Number = i + 1;
        Info = null;
        RowsReshaped();
        RefreshPrompts();
    }

    // A new row starts as a copy of the last working row, since most sets repeat the same weight
    [RelayCommand]
    private void AddRow()
    {
        if (!HasRoomForARow()) return;
        var last = Rows.LastOrDefault(r => !r.IsWarmup);
        InsertRow(Rows.Count, SetKind.Working, last?.WeightText ?? "", last?.RepsText ?? "");
    }

    private bool HasRoomForARow()
    {
        if (Rows.Count < SetService.MaxSetsPerExercisePerDay) return true;
        Error = $"At most {SetService.MaxSetsPerExercisePerDay} sets per exercise per day.";
        return false;
    }

    private void InsertRow(int index, SetKind kind, string weight, string reps)
    {
        _loading = true;
        Rows.Insert(index, new SetRowViewModel(this, index + 1, kind, weight, reps));
        for (var i = 0; i < Rows.Count; i++) Rows[i].Number = i + 1;
        _loading = false;
        Error = null;
        Info = null;
        RowsReshaped();
        RefreshPrompts();
    }

    [RelayCommand]
    private async Task PrefillAsync()
    {
        try
        {
            var last = await _sets.GetLastSessionAsync(UserId, _exerciseId, _today);
            if (last.Count == 0) { Error = null; Info = "No earlier session to copy yet."; return; }

            RebuildRows(last.Select(s => new RowData(
                s.Kind, NumberParsing.FormatKg(s.WeightKg), s.Reps.ToString(CultureInfo.InvariantCulture))));
            Error = null;
            Info = $"Filled from {FormatDate(last[0].Date)}. Change anything, then save.";

        }
        catch (Exception)
        {
            Error = "Could not load your last session.";
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var inputs = new List<SetInput>();
        for (var i = 0; i < Rows.Count; i++)
        {
            var row = Rows[i];
            if (string.IsNullOrWhiteSpace(row.WeightText) && string.IsNullOrWhiteSpace(row.RepsText))
                continue;   // empty rows are ignored

            if (!NumberParsing.TryParseKg(row.WeightText, out var kg) ||
                !NumberParsing.TryParseReps(row.RepsText, out var reps))
            {
                Error = $"Set {i + 1}: enter the weight (0 for bodyweight) and the number of reps.";
                Info = null;
                return;
            }
            inputs.Add(new SetInput(row.IsWarmup ? SetKind.Warmup : SetKind.Working, reps, kg));
        }

        var before = _history.Pr.Current;
        var result = await _sets.SaveDayAsync(UserId, _exerciseId, inputs);
        if (!result.Success) { Error = result.Error; Info = null; return; }

        _history = await _sets.GetHistoryAsync(UserId, _exerciseId);
        await RebuildFromSavedAsync();
        ShowPr();

        var after = _history.Pr.Current;
        var improved = after is not null && after.Kind != PrEntryKind.Manual
                       && PrCalculator.Beats(after.WeightKg, after.Reps, before);
        Error = null;
        IsPrFresh = improved;
        Info = inputs.Count == 0 ? "Cleared today's sets."
             : improved ? (before is null ? $"Saved. First PR: {BestText}." : $"Saved. New PR: {BestText}!")
             : "Saved.";
    }

    // ---------- planned sets ----------

    // + adds a row of that kind (warm-ups go after the last warm-up, working sets at the end);
    // − removes the last row of that kind, an empty one first so nothing you typed is lost
    [RelayCommand]
    private void MoreWarmup()
    {
        if (WarmupSets >= DayExerciseService.MaxWarmupSets)
        {
            Error = $"Warmup sets must be 0 to {DayExerciseService.MaxWarmupSets}.";
            return;
        }
        if (!HasRoomForARow()) return;
        var lastWarmup = Rows.LastOrDefault(r => r.IsWarmup);
        InsertRow(lastWarmup is null ? 0 : Rows.IndexOf(lastWarmup) + 1, SetKind.Warmup, "", "");
    }

    [RelayCommand]
    private void MoreWorking()
    {
        if (WorkingSets >= DayExerciseService.MaxWorkingSets)
        {
            Error = $"Working sets must be 1 to {DayExerciseService.MaxWorkingSets}.";
            return;
        }
        AddRow();
    }

    [RelayCommand] private void FewerWarmup() => RemoveLastOfKind(warmup: true);

    [RelayCommand]
    private void FewerWorking()
    {
        if (WorkingSets <= 1)
        {
            Error = $"Working sets must be 1 to {DayExerciseService.MaxWorkingSets}.";
            return;
        }
        RemoveLastOfKind(warmup: false);
    }

    private void RemoveLastOfKind(bool warmup)
    {
        var ofKind = Rows.Where(r => r.IsWarmup == warmup).ToList();
        var row = ofKind.LastOrDefault(r => string.IsNullOrWhiteSpace(r.WeightText) && string.IsNullOrWhiteSpace(r.RepsText))
                  ?? ofKind.LastOrDefault();
        if (row is null) return;
        Error = null;
        RemoveRow(row);
    }

    // ---------- PR ----------

    private void ShowPr()
    {
        var pr = _history.Pr;
        if (pr.Current is not { } cur)
        {
            BestText = "No PR yet";
            OneRmText = "";
            BestNote = "Your first working set sets it.";
        }
        else
        {
            BestText = $"{NumberParsing.FormatKg(cur.WeightKg)} kg × {cur.Reps}";
            OneRmText = $"≈ {NumberParsing.FormatKg(Math.Round(cur.OneRepMaxKg, 1))} kg est. 1RM";
            BestNote = cur.Kind == PrEntryKind.Manual ? "Edited by you" : $"Set on {FormatDate(cur.Date)}";
        }

        PrEntries.Clear();
        foreach (var e in pr.Entries.Reverse()) PrEntries.Add(new PrRowViewModel(e, this));
        HasPrEntries = PrEntries.Count > 0;

        HasPr = pr.Current is not null;
        if (pr.Current is { } c)
        {
            PrWeight = NumberParsing.FormatKg(c.WeightKg);
            PrReps = c.Reps.ToString(CultureInfo.InvariantCulture);
            PrOneRm = $"{NumberParsing.FormatKg(Math.Round(c.OneRepMaxKg, 1))} kg";
            PrDateText = c.Kind == PrEntryKind.Manual ? $"Edited {FormatShort(c.Date)}"
                       : c.Date == _today ? "Today" : c.Date.ToString("ddd d MMM", CultureInfo.InvariantCulture);
        }
        ShowTopSets();
    }

    private string FormatShort(DateOnly d) => d == _today ? "today" : d.ToString("d MMM", CultureInfo.InvariantCulture);

    /// <summary>The heaviest working set of each of the last sessions (today included), oldest first.</summary>
    private void ShowTopSets()
    {
        var tops = _history.Sets
            .Where(x => x.Kind == SetKind.Working)
            .GroupBy(x => x.Date)
            .Select(g => g.OrderByDescending(x => x.WeightKg).ThenByDescending(x => x.Reps).First())
            .OrderByDescending(x => x.Date).Take(ChartSessions).Reverse().ToList();
        HasTopSets = tops.Count > 1;
        if (!HasTopSets) { TopSets = []; return; }

        // Bars start a little under the lightest so the differences show; the PR day is highlighted
        var lo = tops.Min(x => x.WeightKg) * 0.8;
        var hi = Math.Max(tops.Max(x => x.WeightKg), lo + 1);
        var prDate = _history.Pr.Current is { Kind: not PrEntryKind.Manual } p ? p.Date : (DateOnly?)null;
        TopSets = tops.Select(x => new TopSetBar(
            x.Date.ToString("d MMM", CultureInfo.InvariantCulture),
            NumberParsing.FormatKg(x.WeightKg),
            Math.Max(6, Math.Round((x.WeightKg - lo) / (hi - lo) * ChartHeight)),
            x.Date == prDate)).ToList();
    }

    [RelayCommand]
    private void StartEditPr()
    {
        var cur = _history.Pr.Current;
        PrWeightText = cur is null ? "" : NumberParsing.FormatKg(cur.WeightKg);
        PrRepsText = cur is null ? "" : cur.Reps.ToString(CultureInfo.InvariantCulture);
        PrOneRmInput = "";      // empty = estimate from weight and reps
        Error = null;
        ClearPrErrors();
        Info = null;
        IsEditingPr = true;
    }

    [RelayCommand] private void CancelEditPr() { IsEditingPr = false; Error = null; ClearPrErrors(); }

    [RelayCommand]
    private async Task SavePrAsync()
    {
        try
        {
            ClearPrErrors();
            Error = null;
            if (!NumberParsing.TryParseKg(PrWeightText, out var kg)) PrWeightError = "Enter the weight in kg, e.g. 100 or 82.5.";
            if (!NumberParsing.TryParseReps(PrRepsText, out var reps)) PrRepsError = "Enter the number of reps.";
            if (PrWeightError is not null || PrRepsError is not null) return;

            double? oneRm = null;
            if (!string.IsNullOrWhiteSpace(PrOneRmInput))
            {
                if (!NumberParsing.TryParseKg(PrOneRmInput, out var rm))
                {
                    PrOneRmError = "The 1RM must be a number, or leave it empty.";
                    return;
                }
                oneRm = rm;
            }

            var result = await _sets.SetPrAsync(UserId, _exerciseId, kg, reps, oneRm);
            if (!result.Success)
            {
                switch (result.Field)
                {
                    case FormField.Weight: PrWeightError = result.Error; break;
                    case FormField.Reps: PrRepsError = result.Error; break;
                    case FormField.OneRepMax: PrOneRmError = result.Error; break;
                    default: Error = result.Error; break;
                }
                return;
            }

            _history = await _sets.GetHistoryAsync(UserId, _exerciseId);
            ShowPr();
            RefreshPrompts();
            IsEditingPr = false;

            // Sets logged today are applied after today's edit, so a heavier one today wins
            var cur = _history.Pr.Current;
            if (cur is null || cur.WeightKg != Math.Round(kg, 2) || cur.Reps != reps)
            {
                Info = null;
                Error = "Sets you logged today are heavier than that, so the PR stays. Edit those sets to lower it today.";
            }
            else
            {
                Error = null;
                Info = "PR updated.";
            }

        }
        catch (Exception)
        {
            Error = "Could not save the PR.";
        }
    }

    internal async Task RemoveAdjustmentAsync(DateOnly date)
    {
        try
        {
            if (!await _sets.RemovePrAdjustmentAsync(UserId, _exerciseId, date))
            {
                Error = "Could not undo that edit.";
                return;
            }

            _history = await _sets.GetHistoryAsync(UserId, _exerciseId);
            ShowPr();
            RefreshPrompts();
            Error = null;
            Info = "Edit undone. The PR follows your logged sets again.";

        }
        catch (Exception)
        {
            Error = "Could not undo that edit.";
        }
    }

    // Waits for a plan save still in flight, so the day list shows the new set counts
    [RelayCommand]
    private async Task BackAsync()
    {
        await _planSave;
        _shell().ShowDay(_day, _splitName);
    }
    [RelayCommand] private void ShowDashboard() => _shell().ShowHome();
}