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
    [NotifyPropertyChangedFor(nameof(KindLabel))]
    public partial bool IsWarmup { get; set; }

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

public partial class ExerciseViewModel : ViewModelBase
{
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
    [ObservableProperty] public partial int WarmupSets { get; set; }
    [ObservableProperty] public partial int WorkingSets { get; set; } = 3;

    [ObservableProperty] public partial string BestText { get; set; } = "No PR yet";
    [ObservableProperty] public partial string OneRmText { get; set; } = "";
    [ObservableProperty] public partial string BestNote { get; set; } = "";
    [ObservableProperty] public partial bool HasPrEntries { get; set; }

    [ObservableProperty] public partial bool IsEditingPr { get; set; }
    [ObservableProperty] public partial string PrWeightText { get; set; } = "";
    [ObservableProperty] public partial string PrRepsText { get; set; } = "";
    [ObservableProperty] public partial string PrOneRmInput { get; set; } = "";

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
        _today = DateOnly.FromDateTime(DateTime.Now);
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
            WarmupSets = row.WarmupSets;
            WorkingSets = row.WorkingSets;

            _history = await _sets.GetHistoryAsync(UserId, _exerciseId);
            await RebuildFromSavedAsync();
            ShowPr();
        }
        catch (Exception)
        {
            Error = "Could not load this exercise.";
        }
    }

    // ---------- today's sets ----------

    private IEnumerable<RowData> PlanRows() =>
        Enumerable.Repeat(new RowData(SetKind.Warmup, "", ""), WarmupSets)
            .Concat(Enumerable.Repeat(new RowData(SetKind.Working, "", ""), WorkingSets));

    // Sets saved today, or empty rows following the plan when nothing is saved yet
    private async Task RebuildFromSavedAsync()
    {
        var saved = await _sets.GetDayAsync(UserId, _exerciseId, _today);
        RebuildRows(saved.Count > 0
            ? saved.Select(s => new RowData(s.Kind, NumberParsing.FormatKg(s.WeightKg), s.Reps.ToString(CultureInfo.InvariantCulture)))
            : PlanRows());
    }

    private void RebuildRows(IEnumerable<RowData> data)
    {
        _loading = true;
        Rows.Clear();
        var n = 0;
        foreach (var d in data) Rows.Add(new SetRowViewModel(this, ++n, d.Kind, d.Weight, d.Reps));
        _loading = false;
        RefreshPrompts();
    }

    internal void RowChanged()
    {
        if (_loading) return;
        Info = null;
        RefreshPrompts();
    }

    private void RefreshPrompts()
    {
        var draft = Rows.Select(r => r.ToDraft()).ToList();
        for (var i = 0; i < Rows.Count; i++)
            Rows[i].ApplyPrompt(PrPrompts.ForRow(draft, i, _history.Sets, _history.Adjustments, _today));
    }

    internal void RemoveRow(SetRowViewModel row)
    {
        Rows.Remove(row);
        for (var i = 0; i < Rows.Count; i++) Rows[i].Number = i + 1;
        Info = null;
        RefreshPrompts();
    }

    // A new row starts as a copy of the last working row, since most sets repeat the same weight
    [RelayCommand]
    private void AddRow()
    {
        if (Rows.Count >= SetService.MaxSetsPerExercisePerDay)
        {
            Error = $"At most {SetService.MaxSetsPerExercisePerDay} sets per exercise per day.";
            return;
        }

        var last = Rows.LastOrDefault(r => !r.IsWarmup);
        _loading = true;
        Rows.Add(new SetRowViewModel(this, Rows.Count + 1, SetKind.Working, last?.WeightText ?? "", last?.RepsText ?? ""));
        _loading = false;
        Error = null;
        Info = null;
        RefreshPrompts();
    }

    [RelayCommand]
    private async Task PrefillAsync()
    {
        var last = await _sets.GetLastSessionAsync(UserId, _exerciseId, _today);
        if (last.Count == 0) { Error = null; Info = "No earlier session to copy yet."; return; }

        RebuildRows(last.Select(s => new RowData(
            s.Kind, NumberParsing.FormatKg(s.WeightKg), s.Reps.ToString(CultureInfo.InvariantCulture))));
        Error = null;
        Info = $"Filled from {FormatDate(last[0].Date)}. Change anything, then save.";
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
        Info = inputs.Count == 0 ? "Cleared today's sets."
             : improved ? (before is null ? $"Saved. First PR: {BestText}." : $"Saved. New PR: {BestText}!")
             : "Saved.";
    }

    // ---------- planned sets ----------

    [RelayCommand] private Task MoreWarmupAsync() => ChangePlanAsync(+1, 0);
    [RelayCommand] private Task FewerWarmupAsync() => ChangePlanAsync(-1, 0);
    [RelayCommand] private Task MoreWorkingAsync() => ChangePlanAsync(0, +1);
    [RelayCommand] private Task FewerWorkingAsync() => ChangePlanAsync(0, -1);

    private async Task ChangePlanAsync(int deltaWarmup, int deltaWorking)
    {
        var warmup = WarmupSets + deltaWarmup;
        var working = WorkingSets + deltaWorking;

        var r = await _days.SetPlannedSetsAsync(UserId, _day.SplitDayId, _exerciseId, warmup, working);
        if (!r.Success) { Error = r.Error; return; }

        Error = null;
        WarmupSets = warmup;
        WorkingSets = working;

        // An untouched draft follows the plan; anything you typed is left alone
        if (Rows.All(x => string.IsNullOrWhiteSpace(x.WeightText) && string.IsNullOrWhiteSpace(x.RepsText)))
            RebuildRows(PlanRows());
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
    }

    [RelayCommand]
    private void StartEditPr()
    {
        var cur = _history.Pr.Current;
        PrWeightText = cur is null ? "" : NumberParsing.FormatKg(cur.WeightKg);
        PrRepsText = cur is null ? "" : cur.Reps.ToString(CultureInfo.InvariantCulture);
        PrOneRmInput = "";      // empty = estimate from weight and reps
        Error = null;
        Info = null;
        IsEditingPr = true;
    }

    [RelayCommand] private void CancelEditPr() { IsEditingPr = false; Error = null; }

    [RelayCommand]
    private async Task SavePrAsync()
    {
        if (!NumberParsing.TryParseKg(PrWeightText, out var kg) || !NumberParsing.TryParseReps(PrRepsText, out var reps))
        {
            Error = "Enter the weight and the number of reps.";
            return;
        }

        double? oneRm = null;
        if (!string.IsNullOrWhiteSpace(PrOneRmInput))
        {
            if (!NumberParsing.TryParseKg(PrOneRmInput, out var rm))
            {
                Error = "The 1RM must be a number, or leave it empty.";
                return;
            }
            oneRm = rm;
        }

        var result = await _sets.SetPrAsync(UserId, _exerciseId, kg, reps, oneRm);
        if (!result.Success) { Error = result.Error; return; }

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

    internal async Task RemoveAdjustmentAsync(DateOnly date)
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

    [RelayCommand] private void Back() => _shell().ShowDay(_day, _splitName);
}