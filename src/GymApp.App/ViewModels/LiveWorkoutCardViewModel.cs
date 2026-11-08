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

/// <summary>One set chip on the live card: "W 60 × 5", "1 90 × 8", "2 — × —". Its state is only in its style: logged
/// (tinted), the set to do now (outlined; dashed while another set is being changed), being changed (inverse), later
/// (dashed, not tappable).</summary>
public partial class LiveSetChipViewModel(string text, bool isLogged, bool isNow, bool isEditing, bool isAnotherEditing,
    string accessibleName, Action? tap) : ObservableObject
{
    public string Text { get; } = text;
    public bool IsLogged { get; } = isLogged && !isEditing;
    public bool IsNow { get; } = isNow && !isAnotherEditing;
    public bool IsNowWhileEditing { get; } = isNow && isAnotherEditing;
    public bool IsEditing { get; } = isEditing;
    public bool IsLater { get; } = tap is null;
    public bool IsTappable => tap is not null;
    public string AccessibleName { get; } = accessibleName;

    [RelayCommand] private void Tap() => tap?.Invoke();
}

/// <summary>One exercise in the card's list: tap it to do it now.</summary>
public partial class LiveExerciseRowViewModel(string name, string status, bool isFilled, bool isCurrent, Action pick) : ObservableObject
{
    public string Name { get; } = name;
    /// <summary>"Now", "Editing", "Done", "Skipped", "2 / 3 sets" or "3 sets".</summary>
    public string Status { get; } = status;
    public bool IsFilled { get; } = isFilled;
    public bool IsCurrent { get; } = isCurrent;
    public bool IsOpen => !IsFilled && !IsCurrent;

    [RelayCommand] private void Pick() => pick();
}

/// <summary>
/// Phase 18's live workout card on Home (the "Home during a workout" artboard): one exercise and one set at a time,
/// Next, Skip for now, the exercises to pick from, Finish once half are filled, End workout below that. Nothing here
/// says the workout already counts or mentions points while it runs (it's logged silently at half); the Finish sheet
/// pushes for the whole workout. The service does the saving; this keeps only what's being typed.
/// </summary>
public partial class LiveWorkoutCardViewModel : ViewModelBase
{
    private readonly LiveWorkoutService _service;
    private readonly SetService _sets;
    private readonly ILocationProvider _location;
    private readonly Action _addGym;
    private readonly int _userId;
    private readonly Action<LiveWorkoutInfo> _changed;
    private readonly Func<LiveEndResult, Task> _ended;

    public LiveWorkoutCardViewModel(LiveWorkoutService service, SetService sets, ILocationProvider location, int userId,
        LiveWorkoutInfo info, Action<LiveWorkoutInfo> changed, Func<LiveEndResult, Task> ended, Action addGym)
    {
        _service = service;
        _sets = sets;
        _location = location;
        _addGym = addGym;
        _userId = userId;
        _changed = changed;
        _ended = ended;
        Info = info;
        Render();
        _ = LoadBarsAsync();
        _ = CheckLocationAsync();
    }

    // ===== Verified at gym (18c): read at Start and at each Next until found, then never again =====

    /// <summary>"At Iron Gym ✓" once the phone was found at one of the user's gyms.</summary>
    [ObservableProperty] public partial string? VerifiedText { get; set; }
    /// <summary>A fix came, and it isn't near any of the user's gyms: "Add this gym".</summary>
    [ObservableProperty] public partial bool IsNotAtGym { get; set; }
    /// <summary>Location permission refused: the workout runs unverified, with a link to Settings.</summary>
    [ObservableProperty] public partial bool IsLocationOff { get; set; }
    /// <summary>Location is allowed, but only approximate: too vague to verify, so it isn't read. Links to Settings.</summary>
    [ObservableProperty] public partial bool IsPreciseOff { get; set; }
    /// <summary>No fix within 10 seconds yet, or one too vague to count (indoors); tried again at the next set.</summary>
    [ObservableProperty] public partial bool IsLocationPending { get; set; }

    public static readonly TimeSpan FixTimeout = TimeSpan.FromSeconds(10);
    private bool _checkingLocation;

    private async Task CheckLocationAsync()
    {
        ShowVerified();
        if (Info.IsVerified || !_location.IsSupported || _checkingLocation) return;
        if (!_location.HasPermission || !_location.IsPrecise)
        {
            IsLocationOff = !_location.HasPermission;
            IsPreciseOff = _location.HasPermission;
            IsNotAtGym = IsLocationPending = false;
            return;
        }
        IsLocationOff = IsPreciseOff = false;
        _checkingLocation = true;
        try
        {
            var fix = await _location.GetFixAsync(FixTimeout);
            // No fix, or one too vague to count (often indoors): not a sign of being elsewhere, so try again next set
            if (fix is null || !GymProximity.IsUsable(fix)) { IsLocationPending = !IsNotAtGym; return; }
            var info = await _service.VerifyAsync(_userId, fix);
            if (info is { IsVerified: true }) Apply(info, keepFields: true);
            else { IsNotAtGym = true; IsLocationPending = false; }
        }
        catch (Exception) { /* unverified this time; the next set tries again */ }
        finally
        {
            _checkingLocation = false;
            ShowVerified();
        }
    }

    private void ShowVerified()
    {
        VerifiedText = Info.VerifiedGymName is { } gym ? $"At {gym} ✓" : null;
        if (Info.IsVerified) IsNotAtGym = IsLocationOff = IsPreciseOff = IsLocationPending = false;
    }

    [RelayCommand] private void AddThisGym() => _addGym();
    [RelayCommand] private void OpenLocationSettings() => _location.OpenSettings();

    public LiveWorkoutInfo Info { get; private set; }

    // ===== The exercise on the card =====
    [ObservableProperty] public partial bool HasCurrent { get; set; }
    [ObservableProperty] public partial bool AllDone { get; set; }
    [ObservableProperty] public partial string Kicker { get; set; } = "";
    [ObservableProperty] public partial string ExerciseName { get; set; } = "";
    [ObservableProperty] public partial string SetLabel { get; set; } = "";
    [ObservableProperty] public partial string? LastTimeText { get; set; }
    public ObservableCollection<LiveSetChipViewModel> Chips { get; } = new();
    [ObservableProperty] public partial bool ShowChipHint { get; set; }
    [ObservableProperty] public partial bool CanSkip { get; set; }

    /// <summary>The exercise on the card has all its sets: "Every set is logged" and "Next: Leg press".</summary>
    [ObservableProperty] public partial bool IsCurrentFilled { get; set; }
    [ObservableProperty] public partial bool ShowFocusDone { get; set; }
    [ObservableProperty] public partial string BackLabel { get; set; } = "";

    // ===== The fields =====
    [ObservableProperty] public partial bool ShowInputs { get; set; }
    [ObservableProperty] public partial string WeightText { get; set; } = "";
    [ObservableProperty] public partial string RepsText { get; set; } = "";
    [ObservableProperty] public partial string RepsPlaceholder { get; set; } = "";
    [ObservableProperty] public partial bool IsValid { get; set; }
    [ObservableProperty] public partial bool IsSaving { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }

    /// <summary>A logged set is open for changes (its index among today's sets, warm-ups first).</summary>
    [ObservableProperty] public partial int? EditingIndex { get; set; }
    [ObservableProperty] public partial bool IsEditing { get; set; }
    /// <summary>The open set's weight or reps differ: Cancel / Save set instead of "Logged ✓".</summary>
    [ObservableProperty] public partial bool EditChanged { get; set; }
    [ObservableProperty] public partial bool EditUnchanged { get; set; }
    [ObservableProperty] public partial bool NotEditing { get; set; } = true;
    private SetEntry? _editOriginal;

    partial void OnWeightTextChanged(string value)
    {
        UpdateTotalHint();
        Validate();
    }
    partial void OnRepsTextChanged(string value) => Validate();

    // ===== Bar exercises: the weight typed as the total, or per side plus the bar (like the Exercise screen) =====
    [ObservableProperty] public partial bool UsesBar { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTotalMode), nameof(WeightModeIndex), nameof(WeightLabel))]
    public partial bool IsPerSide { get; set; }

    public bool IsTotalMode => !IsPerSide;
    /// <summary>For the Total / Per side pill: 0 = total, 1 = per side.</summary>
    public int WeightModeIndex => IsPerSide ? 1 : 0;
    public string WeightLabel => IsPerSide ? "Per side (kg)" : "Weight (kg)";
    [ObservableProperty] public partial string BarText { get; set; } = "";
    [ObservableProperty] public partial string? BarError { get; set; }
    /// <summary>The exercise already has a set today: Total / Per side and the bar are fixed for the rest of it, so its
    /// sets are all typed the same way. <see cref="LockedModeText"/> says which.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangeBar))]
    public partial bool IsBarLocked { get; set; }

    public bool CanChangeBar => UsesBar && !IsBarLocked;
    public bool ShowLockedBar => UsesBar && IsBarLocked;
    [ObservableProperty] public partial string LockedModeText { get; set; } = "";
    partial void OnUsesBarChanged(bool value)
    {
        OnPropertyChanged(nameof(CanChangeBar));
        OnPropertyChanged(nameof(ShowLockedBar));
    }
    partial void OnIsBarLockedChanged(bool value) => OnPropertyChanged(nameof(ShowLockedBar));

    /// <summary>Per side: "= 140 kg (60 × 2 + 20 bar)" under the weight.</summary>
    [ObservableProperty] public partial string? TotalHint { get; set; }

    /// <summary>The bar exercises' settings, loaded once; the exercise the shown mode belongs to.</summary>
    private readonly Dictionary<int, BarSetting> _bars = new();
    private int? _barExerciseId;
    private bool _syncingBar;
    private Task _barSave = Task.CompletedTask;

    private double? BarKg => NumberParsing.TryParseKg(BarText, out var kg) ? kg : null;

    // ===== The list, Finish and End =====
    public ObservableCollection<LiveExerciseRowViewModel> Rows { get; } = new();
    [ObservableProperty] public partial bool CanFinish { get; set; }
    [ObservableProperty] public partial bool CanEnd { get; set; }
    [ObservableProperty] public partial bool CanNext { get; set; }
    [ObservableProperty] public partial bool CanSave { get; set; }

    [ObservableProperty] public partial bool IsFinishOpen { get; set; }
    [ObservableProperty] public partial bool IsEndOpen { get; set; }
    [ObservableProperty] public partial string FinishTitle { get; set; } = "";
    [ObservableProperty] public partial string FinishText { get; set; } = "";
    [ObservableProperty] public partial bool FinishPartial { get; set; }
    [ObservableProperty] public partial string? SheetError { get; set; }

    public int Filled => Info.Status.Filled;
    public int Total => Info.Status.Total;
    /// <summary>"2 of 4 exercises", under the arc.</summary>
    public string ProgressText => $"{Filled} of {Total} exercise{(Total == 1 ? "" : "s")}";
    /// <summary>The share of exercises filled, for the arc.</summary>
    public double Progress => Total == 0 ? 0 : (double)Filled / Total;

    /// <summary>Back closes a sheet, then a set being changed.</summary>
    public bool CloseOnBack()
    {
        if (IsFinishOpen || IsEndOpen) { CloseSheet(); return true; }
        if (IsEditing) { CancelEdit(); return true; }
        return false;
    }

    /// <summary>The exercise on the card, for "Bench press is next."; null when every exercise is done.</summary>
    public string? NextExerciseName => CurrentInfo?.Name;

    private LiveExercise? Current => LiveWorkoutRules.Current(Info.Plan, Info.CurrentExerciseId, Info.SkippedExerciseIds);

    private CheckInExerciseInfo? CurrentInfo => Current is { } c ? Info.Exercises.First(e => e.ExerciseId == c.ExerciseId) : null;

    private void Apply(LiveWorkoutInfo info, bool keepFields = false)
    {
        Info = info;
        Render(keepFields);
        _changed(info);
    }

    private void Render(bool keepFields = false)
    {
        var plan = Info.Plan;
        var order = LiveWorkoutRules.Order(plan, Info.SkippedExerciseIds);
        var current = Current;
        var info = CurrentInfo;

        HasCurrent = current is not null;
        AllDone = current is null;
        IsCurrentFilled = current is { IsFilled: true };
        var nextUp = order.FirstOrDefault(e => !e.IsFilled && e.ExerciseId != current?.ExerciseId);
        BackLabel = nextUp is null ? "Back to the workout" : $"Next: {NameOf(nextUp.ExerciseId)}";

        Chips.Clear();
        if (current is not null && info is not null)
        {
            Kicker = $"EXERCISE {order.ToList().FindIndex(e => e.ExerciseId == current.ExerciseId) + 1} OF {plan.Count}";
            ExerciseName = info.Name;
            var top = info.LastSession.Where(s => s.Kind == SetKind.Working)
                .OrderByDescending(s => s.WeightKg).ThenByDescending(s => s.Reps).FirstOrDefault();
            LastTimeText = top is null ? null : $"{Kg(top.WeightKg)} kg × {top.Reps}";
            BuildChips(current, info);
        }
        else
        {
            Kicker = "";
            ExerciseName = "";
            LastTimeText = null;
        }

        var next = current is null ? null : LiveWorkoutRules.NextSet(current);
        SetLabel = EditingIndex is { } e && info is not null ? $"Changing {SetName(info, e, lower: true)}"
            : current is null ? ""
            : next is null ? "All sets logged"
            : next.Kind == SetKind.Warmup
                ? current.WarmupSets > 1 ? $"Warm-up {next.Number} of {current.WarmupSets}" : "Warm-up"
                : $"Set {next.Number} of {current.PlannedWorking}";

        ShowInputs = current is not null && (next is not null || EditingIndex is not null);
        ShowChipHint = next is not null && EditingIndex is null && current!.TodaySets.Count > 0;
        CanSkip = next is not null && EditingIndex is null && order.Count(x => !x.IsFilled) > 1;
        IsEditing = EditingIndex is not null;
        NotEditing = !IsEditing;
        ShowFocusDone = IsCurrentFilled && !IsEditing;
        SyncBar(info);
        IsBarLocked = current is { TodaySets.Count: > 0 };
        LockedModeText = !IsPerSide ? "Total weight"
            : BarKg is { } lockedBar and > 0 ? $"Per side · {Kg(lockedBar)} kg bar" : "Per side";
        if (!keepFields) Prefill(current, info, next);

        Rows.Clear();
        foreach (var x in order)
        {
            var isCurrent = x.ExerciseId == current?.ExerciseId;
            var skipped = Info.SkippedExerciseIds.Contains(x.ExerciseId) && !x.IsFilled && !isCurrent;
            var done = Math.Min(x.LoggedWorking, x.PlannedWorking);
            var status = isCurrent ? (x.IsFilled ? "Editing" : "Now")
                : x.IsFilled ? "Done"
                : skipped ? "Skipped"
                : x.TodaySets.Count > 0 ? $"{done} / {x.PlannedWorking} sets"
                : $"{x.PlannedWorking} sets";
            var id = x.ExerciseId;
            Rows.Add(new LiveExerciseRowViewModel(NameOf(id), status, x.IsFilled, isCurrent, () => _ = PickAsync(id)));
        }

        CanFinish = current is not null && Info.Status.CanLog;
        CanEnd = !Info.Status.CanLog;
        OnPropertyChanged(nameof(Filled));
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(Progress));
        Validate();
    }

    private string NameOf(int exerciseId) => Info.Exercises.FirstOrDefault(e => e.ExerciseId == exerciseId)?.Name ?? "Exercise";

    /// <summary>The chips: today's sets in the card's order (warm-ups first), then the planned ones still to do.</summary>
    private void BuildChips(LiveExercise current, CheckInExerciseInfo info)
    {
        var logged = LiveWorkoutService.Ordered(current.TodaySets);
        var warmups = logged.Count(s => s.Kind == SetKind.Warmup);
        var working = logged.Count - warmups;
        var next = LiveWorkoutRules.NextSet(current);
        var slots = new List<(SetKind Kind, int Number, SetEntry? Set, int? Index)>();
        for (var i = 0; i < Math.Max(current.WarmupSets, warmups); i++)
            slots.Add((SetKind.Warmup, i + 1, i < warmups ? logged[i] : null, i < warmups ? i : null));
        for (var i = 0; i < Math.Max(current.PlannedWorking, working); i++)
            slots.Add((SetKind.Working, i + 1, i < working ? logged[warmups + i] : null, i < working ? warmups + i : null));

        foreach (var (kind, number, set, index) in slots)
        {
            var tag = kind == SetKind.Warmup ? "W" : number.ToString(CultureInfo.InvariantCulture);
            var isNow = set is null && next is { } n && n.Kind == kind && n.Number == number;
            var editing = index is not null && index == EditingIndex;
            var name = SetName(info, kind, number);
            Action? tap = set is not null && index is { } at ? () => StartEdit(at, set)
                : isNow ? CancelEdit
                : null;
            Chips.Add(new LiveSetChipViewModel(
                set is null ? $"{tag} — × —" : $"{tag} {Kg(set.WeightKg)} × {set.Reps}",
                set is not null, isNow, editing, EditingIndex is not null && !editing,
                set is null ? name : $"Change {name.ToLowerInvariant()}, {Kg(set.WeightKg)} kilos × {set.Reps}", tap));
        }
    }

    private static string SetName(CheckInExerciseInfo info, SetKind kind, int number) =>
        kind == SetKind.Warmup ? (info.WarmupSets > 1 ? $"Warm-up {number}" : "Warm-up") : $"Set {number}";

    private string SetName(CheckInExerciseInfo info, int index, bool lower)
    {
        var sets = LiveWorkoutService.Ordered(info.TodaySets);
        if (index >= sets.Count) return "set";
        var set = sets[index];
        var number = sets.Take(index + 1).Count(s => s.Kind == set.Kind);
        var name = SetName(info, set.Kind, number);
        return lower ? name.ToLowerInvariant() : name;
    }

    /// <summary>The weight from the set before it today (same kind), else the same set last time; reps stay empty with
    /// last time's as the placeholder.</summary>
    private void Prefill(LiveExercise? current, CheckInExerciseInfo? info, LiveSet? next)
    {
        Error = null;
        if (EditingIndex is not null) return;
        if (current is null || info is null || next is null)
        {
            WeightText = "";
            RepsText = "";
            RepsPlaceholder = "";
            return;
        }
        var today = current.TodaySets.Where(s => s.Kind == next.Kind).OrderBy(s => s.SetNumber).LastOrDefault();
        var last = info.LastSession.Where(s => s.Kind == next.Kind).ToList();
        var same = last.ElementAtOrDefault(next.Number - 1) ?? last.LastOrDefault();
        WeightText = today is not null ? ShowWeight(today.WeightKg) : same is not null ? ShowWeight(same.WeightKg) : "";
        RepsText = "";
        RepsPlaceholder = same?.Reps.ToString(CultureInfo.InvariantCulture) ?? "";
    }

    private SetInput? Input(SetKind kind) =>
        TryReadTotal(WeightText, out var kg) && NumberParsing.TryParseReps(RepsText, out var reps)
            ? new SetInput(kind, reps, kg)
            : null;

    /// <summary>The total the weight field stands for, whichever way it's typed.</summary>
    private bool TryReadTotal(string text, out double totalKg)
    {
        if (!NumberParsing.TryParseKg(text, out totalKg)) return false;
        if (!IsPerSide) return true;
        if (BarError is not null) return false;
        totalKg = BarWeight.Total(totalKg, BarKg);
        return true;
    }

    /// <summary>A total as the weight field shows it: the total, or what goes on each side.</summary>
    private string ShowWeight(double totalKg) => Kg(IsPerSide ? BarWeight.PerSide(totalKg, BarKg) : totalKg);

    /// <summary>Shows the mode saved for the exercise on the card, keeping what was typed as the same total.</summary>
    private void SyncBar(CheckInExerciseInfo? info)
    {
        var id = info?.ExerciseId;
        if (id == _barExerciseId) return;
        var typed = TryReadTotal(WeightText, out var total) ? total : (double?)null;
        _barExerciseId = id;
        _syncingBar = true;
        UsesBar = info is not null && BarWeight.UsesBar(info.Name);
        var setting = id is { } x && UsesBar ? _bars.GetValueOrDefault(x, BarSetting.Total) : BarSetting.Total;
        IsPerSide = setting.PerSide;
        BarText = setting.BarKg is { } bar ? Kg(bar) : "";
        BarError = null;
        _syncingBar = false;
        if (typed is { } kg) WeightText = ShowWeight(kg);
        UpdateTotalHint();
    }

    private async Task LoadBarsAsync()
    {
        try
        {
            foreach (var e in Info.Exercises.Where(e => BarWeight.UsesBar(e.Name)))
                _bars[e.ExerciseId] = await _sets.GetBarSettingAsync(_userId, e.ExerciseId);
            _barExerciseId = -1;   // shows the loaded mode for the exercise on the card
            SyncBar(CurrentInfo);
            Validate();
        }
        catch (Exception) { /* the total, as before */ }
    }

    [RelayCommand] private void SetTotalMode() => SwitchWeightMode(perSide: false);
    [RelayCommand] private void SetPerSideMode() => SwitchWeightMode(perSide: true);

    // What you typed keeps its meaning: 140 total becomes 60 per side on a 20 kg bar, and back
    private void SwitchWeightMode(bool perSide)
    {
        if (IsPerSide == perSide || !CanChangeBar) return;
        var typed = TryReadTotal(WeightText, out var total) ? total : (double?)null;
        _syncingBar = true;
        IsPerSide = perSide;
        _syncingBar = false;
        if (typed is { } kg) WeightText = ShowWeight(kg);
        UpdateTotalHint();
        Validate();
        QueueBarSave();
    }

    // A new bar weight keeps the per side number, so the total changes with it
    partial void OnBarTextChanged(string value)
    {
        if (_syncingBar || IsBarLocked) return;
        BarError = string.IsNullOrWhiteSpace(value) || BarKg is not null ? null : "Enter the bar in kg, or leave it empty.";
        UpdateTotalHint();
        Validate();
        if (BarError is null) QueueBarSave();
    }

    private void UpdateTotalHint()
    {
        if (!IsPerSide || BarError is not null || !NumberParsing.TryParseKg(WeightText, out var side)) { TotalHint = null; return; }
        var bar = BarKg ?? 0;
        TotalHint = $"= {Kg(BarWeight.Total(side, bar))} kg ({Kg(side)} × 2{(bar > 0 ? $" + {Kg(bar)} bar" : "")})";
    }

    // Saves run one after another (typing the bar saves on each key), so the last value wins
    private void QueueBarSave()
    {
        if (_barExerciseId is not { } id) return;
        var setting = new BarSetting(IsPerSide, BarKg);
        _bars[id] = setting;
        var previous = _barSave;
        _barSave = SaveBarAsync(previous, id, setting);
    }

    private async Task SaveBarAsync(Task previous, int exerciseId, BarSetting setting)
    {
        try
        {
            await previous;
            var r = await _sets.SaveBarSettingAsync(_userId, exerciseId, setting);
            if (!r.Success) BarError = r.Error;
        }
        catch (Exception)
        {
            BarError = "Could not save the bar weight.";
        }
    }

    private void Validate()
    {
        IsValid = Input(SetKind.Working) is not null;
        EditChanged = IsEditing && _editOriginal is { } o && (!TryReadTotal(WeightText, out var kg)
            || Math.Abs(kg - o.WeightKg) > 0.001 || RepsText.Trim() != o.Reps.ToString(CultureInfo.InvariantCulture));
        EditUnchanged = IsEditing && !EditChanged;
        CanNext = IsValid && !IsSaving && ShowInputs && !IsEditing;
        CanSave = IsValid && !IsSaving && IsEditing;
    }

    private void StartEdit(int index, SetEntry set)
    {
        EditingIndex = index;
        _editOriginal = set;
        Render(keepFields: true);
        WeightText = ShowWeight(set.WeightKg);
        RepsText = set.Reps.ToString(CultureInfo.InvariantCulture);
        Error = null;
        Validate();
    }

    /// <summary>"Logged ✓" or Cancel: back to the set to do now.</summary>
    [RelayCommand]
    private void CancelEdit()
    {
        if (EditingIndex is null) return;
        EditingIndex = null;
        _editOriginal = null;
        Render();
    }

    [RelayCommand]
    private Task NextAsync() => SaveAsync(null);

    [RelayCommand]
    private Task SaveEditAsync() => SaveAsync(EditingIndex);

    private async Task SaveAsync(int? replaceIndex)
    {
        if (Current is not { } current) return;
        var kind = replaceIndex is null ? LiveWorkoutRules.NextSet(current)?.Kind : SetKind.Working;
        if (kind is not { } k) return;
        if (Input(k) is not { } set)
        {
            Error = "Enter the weight (0 for bodyweight) and the reps.";
            return;
        }

        IsSaving = true;
        Validate();
        try
        {
            var r = await _service.SaveSetAsync(_userId, current.ExerciseId, set, replaceIndex);
            if (!r.Success || r.Workout is null) { Error = r.Error ?? "Could not save the set."; return; }
            EditingIndex = null;
            _editOriginal = null;
            Apply(r.Workout);
            if (!Info.IsVerified) _ = CheckLocationAsync();
        }
        catch (Exception)
        {
            Error = "Could not save the set.";
        }
        finally
        {
            IsSaving = false;
            Validate();
        }
    }

    [RelayCommand]
    private async Task SkipAsync()
    {
        if (Current is not { } current) return;
        try
        {
            if (await _service.SkipAsync(_userId, current.ExerciseId) is { } info) { EditingIndex = null; Apply(info); }
        }
        catch (Exception) { Error = "Could not skip the exercise."; }
    }

    private async Task PickAsync(int exerciseId)
    {
        try
        {
            if (await _service.PickAsync(_userId, exerciseId) is { } info) { EditingIndex = null; _editOriginal = null; Apply(info); }
        }
        catch (Exception) { Error = "Could not open that exercise."; }
    }

    /// <summary>"Next: Leg press" after changing a finished exercise: back to the plan's order.</summary>
    [RelayCommand]
    private async Task BackToPlanAsync()
    {
        try
        {
            if (await _service.PickAsync(_userId, null) is { } info) { EditingIndex = null; Apply(info); }
        }
        catch (Exception) { Error = "Could not go on to the next exercise."; }
    }

    // ===== Finish and End =====

    [RelayCommand]
    private void OpenFinish()
    {
        SheetError = null;
        var left = Total - Filled;
        var toFull = CheckInRule.RequiredForFull(Total) - Filled;
        // Only a verified workout earns more for finishing it, so an unverified one is asked lightly
        var verified = Info.IsVerified;
        FinishPartial = left > 0;
        FinishTitle = left == 0 ? "Finish workout" : "Finish early?";
        FinishText = left == 0 ? "Every exercise is done."
            : verified && toFull > 0
                ? $"You've done {Filled} of {Total} exercises. Finish now for half points, or do {toFull} more for full points."
                : $"{left} exercise{(left == 1 ? "" : "s")} left. Finish {(left == 1 ? "it" : "them")} before you go?";
        IsFinishOpen = true;
    }

    [RelayCommand] private void OpenEnd() => IsEndOpen = true;

    [RelayCommand]
    private void CloseSheet()
    {
        IsFinishOpen = false;
        IsEndOpen = false;
        SheetError = null;
    }

    [RelayCommand]
    private async Task FinishAsync()
    {
        IsSaving = true;
        try
        {
            // Cardio is timed after the workout (0.18.2): Home asks once it's finished
            var r = await _service.FinishAsync(_userId);
            if (!r.Success) { SheetError = r.Error; return; }
            CloseSheet();
            await _ended(r);
        }
        catch (Exception)
        {
            SheetError = "Could not finish the workout.";
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>End workout, below half: nothing is logged, the sets stay.</summary>
    [RelayCommand]
    private async Task EndAsync()
    {
        IsSaving = true;
        try
        {
            var r = await _service.EndAsync(_userId);
            if (!r.Success) { SheetError = r.Error; return; }
            CloseSheet();
            await _ended(r);
        }
        catch (Exception)
        {
            SheetError = "Could not end the workout.";
        }
        finally
        {
            IsSaving = false;
        }
    }

    private static string Kg(double kg) => NumberParsing.FormatKg(kg);
}
