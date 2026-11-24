using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Core;
using GymApp.Services;

namespace GymApp.App.ViewModels;

/// <summary>One goal type in the Lose / Maintain / Gain selector.</summary>
public partial class GoalTypeOptionViewModel(GoalType type, CaloriesViewModel owner) : ObservableObject
{
    public GoalType Type { get; } = type;
    public string Name => CalorieGoals.DisplayName(Type);

    [ObservableProperty] public partial bool IsSelected { get; set; }

    [RelayCommand] private Task Pick() => owner.ChangeGoalTypeAsync(Type);
}

/// <summary>One day of the "Last 7 days" chart. Heights are in pixels of the chart (see <see cref="CaloriesViewModel.ChartHeight"/>).</summary>
public record CalorieBarViewModel(string Day, string Label, double BarHeight, bool IsLogged, bool IsHit, DateOnly Date = default, bool IsSelected = false,
    double Fraction = 0)
{
    public bool IsOff => IsLogged && !IsHit;
    public bool IsMissing => !IsLogged;
}

/// <summary>One macro's bar on today's card (Phase 27): its grams and how full the bar is, 0 to 1. <see cref="IsOver"/>: past its
/// target (only with one).</summary>
/// <summary>One macro bar: grams eaten, the target when there is one ("90 / 144 g"), how full the bar is, and whether it went over.</summary>
public record MacroBarViewModel(string Name, string GramsText, double Fill, bool IsOver, string AmountText = "")
{
    /// <summary>"90 / 144 g" against a target, else "90 g".</summary>
    public string Amount => string.IsNullOrEmpty(AmountText) ? GramsText : AmountText;
}

/// <summary>
/// The Calories screen: today's log against the goal, the last 7 days, the calorie goal (lose / maintain / gain,
/// the suggestion and your own goal) and the maintenance estimate. The goal and maintenance used to live on Profile;
/// body data still does, so the maintenance card links there.
/// </summary>
public partial class CaloriesViewModel : ViewModelBase
{
    /// <summary>The chart area's height in pixels; bars and the target band are sized against it.</summary>
    public const double ChartHeight = 160;

    /// <summary>The chart's top, in kcal, unless a day goes higher.</summary>
    private const int ChartMinTop = 3000;

    private readonly CalorieService _calories;
    private readonly ProfileService _profiles;
    private readonly SessionService _session;
    private readonly Func<MainViewModel> _shell;
    private readonly FoodService _food;

    public CaloriesViewModel(CalorieService calories, ProfileService profiles, SessionService session, Func<MainViewModel> shell,
        FoodService food, IPlatform platform)
    {
        _food = food;
        HasFoodLog = platform.HasFoodLog;
        _calories = calories;
        _profiles = profiles;
        _session = session;
        _shell = shell;
        GoalTypes = Enum.GetValues<GoalType>().Select(t => new GoalTypeOptionViewModel(t, this)).ToList();
        Loaded = LoadAsync();
    }

    /// <summary>The first load, for tests to await.</summary>
    public Task Loaded { get; }

    private int? UserId => _session.CurrentUser?.Id;
    private static string K(int kcal) => CalorieGoals.K(kcal);

    public string DateText => DateTime.Now.ToString("dddd, d MMMM", CultureInfo.InvariantCulture);
    public IReadOnlyList<GoalTypeOptionViewModel> GoalTypes { get; }

    /// <summary>The selected goal type's position in <see cref="GoalTypes"/>, for the sliding pill.</summary>
    [ObservableProperty] public partial int SelectedGoalIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string? Error { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string? Info { get; set; }
    public bool HasMessage => !string.IsNullOrEmpty(Info) || !string.IsNullOrEmpty(Error);
    [ObservableProperty] public partial string HeaderPill { get; set; } = "";

    // ===== Today =====
    [ObservableProperty] public partial bool HasGoal { get; set; }
    [ObservableProperty] public partial string StatusText { get; set; } = "Not logged yet";
    [ObservableProperty] public partial bool IsHit { get; set; }
    [ObservableProperty] public partial bool IsOff { get; set; }
    [ObservableProperty] public partial bool IsOver { get; set; }
    [ObservableProperty] public partial double RingSweep { get; set; }
    [ObservableProperty] public partial string RingCenter { get; set; } = "—";
    [ObservableProperty] public partial string RingCenterSub { get; set; } = "";
    [ObservableProperty] public partial string GoalKcal { get; set; } = "—";
    [ObservableProperty] public partial string EatenKcal { get; set; } = "—";
    [ObservableProperty] public partial string RangeText { get; set; } = "";
    [ObservableProperty] public partial string ResultText { get; set; } = "";
    [ObservableProperty] public partial string TodayInput { get; set; } = "";
    [ObservableProperty] public partial string? TodayError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLogForm), nameof(ShowLegacyLogForm), nameof(ShowLegacyResult))]
    public partial bool IsLogged { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLogForm), nameof(ShowLegacyLogForm), nameof(ShowLegacyResult))]
    public partial bool IsEditing { get; set; }

    public bool ShowLogForm => !IsLogged || IsEditing;

    // ===== Food log (Phase 26, phones only) =====

    /// <summary>Phones: the food diary and the Add food sheet. The desktop keeps the one-number form.</summary>
    public bool HasFoodLog { get; }

    /// <summary>The desktop's one-box form, which the phones no longer show.</summary>
    public bool ShowLegacyLogForm => !HasFoodLog && ShowLogForm;
    public bool ShowLegacyResult => !HasFoodLog && !ShowLogForm;

    // Phase 27 (phones): today's macro bars, and the line under Add food that opens the sheet at its list
    [ObservableProperty] public partial MacroBarViewModel ProteinBar { get; set; } = new("Protein", "0 g", 0, false);
    [ObservableProperty] public partial MacroBarViewModel CarbsBar { get; set; } = new("Carbs", "0 g", 0, false);
    [ObservableProperty] public partial MacroBarViewModel FatBar { get; set; } = new("Fat", "0 g", 0, false);
    [ObservableProperty] public partial bool HasItems { get; set; }
    [ObservableProperty] public partial string GoalEatenText { get; set; } = "";
    /// <summary>Phones: the Calories card's ring, Goal and Eaten and macro bars, the same model as Home's card.</summary>
    public TodayCaloriesViewModel TodayCard { get; } = new();

    /// <summary>Opens a past day's popup (27d); the shell's by default, tests set their own.</summary>
    public Action<DateOnly>? PastDayOpener { get; set; }

    // ===== Last 7 days =====
    [ObservableProperty] public partial IReadOnlyList<CalorieBarViewModel> Bars { get; set; } = [];
    [ObservableProperty] public partial string HitsText { get; set; } = "";
    [ObservableProperty] public partial string OnTargetText { get; set; } = "";
    [ObservableProperty] public partial double BandBottom { get; set; }
    [ObservableProperty] public partial double BandHeight { get; set; }
    /// <summary>The band as shares of the chart's height (phones: the chart grows with the screen).</summary>
    [ObservableProperty] public partial double BandBottomFraction { get; set; }
    [ObservableProperty] public partial double BandHeightFraction { get; set; }

    // ===== Calorie goal =====
    [ObservableProperty] public partial string GoalText { get; set; } = "";
    [ObservableProperty] public partial string GoalRule { get; set; } = "";
    [ObservableProperty] public partial bool ShowSuggestion { get; set; }
    [ObservableProperty] public partial string SuggestionTitle { get; set; } = "";
    [ObservableProperty] public partial string SuggestionNote { get; set; } = "";
    [ObservableProperty] public partial string UseSuggestionLabel { get; set; } = "Use suggestion";
    [ObservableProperty] public partial string KeepGoalLabel { get; set; } = "Keep my goal";
    [ObservableProperty] public partial bool CanKeepGoal { get; set; }
    [ObservableProperty] public partial string? SuggestionReason { get; set; }
    [ObservableProperty] public partial string CustomGoalInput { get; set; } = "";
    [ObservableProperty] public partial string? GoalError { get; set; }

    // Phones: the two rows under the chart (Phase 27)
    [ObservableProperty] public partial string GoalRowText { get; set; } = "Not set";
    [ObservableProperty] public partial string GoalRowDetail { get; set; } = "";
    [ObservableProperty] public partial string MaintenanceRowText { get; set; } = "Add body data";
    [ObservableProperty] public partial bool HasMacroTargets { get; set; }

    private int? _proteinTarget, _carbsTarget, _fatTarget;

    /// <summary>The goal as last loaded, for the goal sheet.</summary>
    public CalorieGoalInfo? GoalInfo { get; private set; }

    // ===== Maintenance =====
    [ObservableProperty] public partial bool HasMaintenance { get; set; }
    [ObservableProperty] public partial string MaintenanceKcal { get; set; } = "";
    [ObservableProperty] public partial string TdeeText { get; set; } = "";
    [ObservableProperty] public partial string BmrText { get; set; } = "";
    [ObservableProperty] public partial string ActivityFactorText { get; set; } = "";
    [ObservableProperty] public partial string MaintenanceNote { get; set; } = "";

    private int? _consumed;

    /// <summary>Opened from Body data's Set calorie goal (0.27.1): the goal sheet opens once the goal is loaded.
    /// Awaits the first load itself, because that load can finish before the caller gets the view model back.</summary>
    public async Task OpenGoalSheetWhenLoadedAsync()
    {
        await Loaded;
        OpenGoalSheet();
    }

    private async Task LoadAsync()
    {
        if (UserId is not { } me) return;
        try
        {
            var o = await _calories.GetAsync(me);
            if (o is null) { Error = "Could not load your calories."; return; }
            ApplyGoal(o.Goal);
            if (TargetsChangedText(o) is { } changed) Info = changed;
            ApplyToday(o);
            ApplyWeek(o);
            await LoadTodayFoodAsync(me);
        }
        catch (Exception)
        {
            Error = "Could not load your calories.";
        }

        try
        {
            if (await _profiles.GetAsync(me) is { } s) ApplyMaintenance(s);
        }
        catch (Exception)
        {
            Error = "Could not load your maintenance estimate.";
        }
    }

    /// <summary>"Your targets changed to 1,990 kcal (you're down 1.2 kg)." once, after the trend weight moved them.</summary>
    private static string? TargetsChangedText(CalorieOverview o) =>
        o.TargetsUpdate is { } u
            ? $"Your targets changed to {K(u.NewKcal)} kcal (you're {(u.WeightChangeKg < 0 ? "down" : "up")} " +
              $"{Units.FormatWeight(Math.Abs(u.WeightChangeKg), o.Goal.MacroInputs?.Units ?? UnitSystem.Metric)})."
            : null;

    private void ApplyGoal(CalorieGoalInfo g)
    {
        foreach (var t in GoalTypes) t.IsSelected = t.Type == g.GoalType;
        SelectedGoalIndex = GoalTypes.ToList().FindIndex(t => t.Type == g.GoalType);
        var type = CalorieGoals.DisplayName(g.GoalType);

        GoalInfo = g;
        HasGoal = g.Goal is not null;
        GoalRowText = g.Goal is { } rowGoal ? $"{K(rowGoal)} kcal" : "Not set";
        GoalRowDetail = g.Goal is null ? "" : g.MacroMode switch
        {
            MacroMode.Recommended => $"{CalorieGoals.ShortName(g.GoalType)} · recommended",
            MacroMode.Custom => $"{CalorieGoals.ShortName(g.GoalType)} · custom",
            _ => CalorieGoals.ShortName(g.GoalType),
        };
        _proteinTarget = g.ProteinGoalGrams; _carbsTarget = g.CarbsGoalGrams; _fatTarget = g.FatGoalGrams;
        HasMacroTargets = _proteinTarget is not null || _carbsTarget is not null || _fatTarget is not null;
        GoalText = g.Goal is { } goal ? K(goal) : "—";
        GoalRule = g.Goal is { } target
            ? $"A day counts as hit {CalorieGoals.TargetRule(target)}."
            : "No goal set yet. Use the suggestion or set your own.";
        HeaderPill = g.Goal is { } pill ? $"{type} · {K(pill)} kcal" : type;

        // The suggestion box: when the suggestion moved since the user last answered, or simply differs from the goal
        SuggestionReason = g.Suggestion is null ? g.SuggestionReason : null;
        if (g.Suggestion is { } s && (g.SuggestionChanged || s.Kcal != g.Goal))
        {
            ShowSuggestion = true;
            SuggestionTitle = $"Suggested for {type.ToLowerInvariant()}: {K(s.Kcal)} kcal / day";
            SuggestionNote = s.Note ?? (g.SuggestionChanged && g.Goal is { } cur
                ? $"Your suggestion changed. Your goal is still {K(cur)} kcal."
                : "An estimate from your maintenance calories.");
            UseSuggestionLabel = $"Use {K(s.Kcal)} kcal";
            CanKeepGoal = g.SuggestionChanged && g.Goal is not null;   // "Keep" answers a changed suggestion
            KeepGoalLabel = g.Goal is { } keep ? $"Keep {K(keep)}" : "Keep my goal";
        }
        else
        {
            ShowSuggestion = false;
        }
    }

    private void ApplyToday(CalorieOverview o)
    {
        var t = o.Today;
        var goal = t?.Goal ?? o.Goal.Goal;   // a logged day keeps the goal it was logged against
        _consumed = t?.Consumed;

        IsLogged = t is not null;
        GoalEatenText = goal is { } ge ? $"Goal {K(ge)} · eaten {K(t?.Consumed ?? 0)}" : $"No goal yet · eaten {K(t?.Consumed ?? 0)}";
        GoalKcal = goal is { } gk ? K(gk) : "—";
        TodayCard.Apply(o);
        EatenKcal = t is null ? "—" : K(t.Consumed);

        if (goal is { } g)
        {
            var (lo, hi) = CalorieGoals.TargetRange(g);
            RangeText = $"{K(lo)}–{K(hi)}";
            IsHit = t is { Hit: true };
            IsOver = t is not null && t.Consumed > hi;
            IsOff = t is not null && !IsHit;
            RingSweep = t is null ? 0 : 360 * Math.Min(1, (double)t.Consumed / g);

            var left = g - (t?.Consumed ?? 0);
            RingCenter = t is null ? K(g) : left >= 0 ? K(left) : "+" + K(-left);
            RingCenterSub = t is null ? "kcal to eat today" : left >= 0 ? "kcal left" : "kcal over goal";
            StatusText = t is null ? "Not logged yet"
                       : IsHit ? $"On target · +{PointsCalculator.CalorieTargetPoints} pts"
                       : t.Consumed < lo ? "Under target" : "Over target";
            ResultText = t is null ? ""
                       : IsHit ? $"Logged {K(t.Consumed)} kcal. Nice, that is on target."
                       : t.Consumed < lo ? $"Logged {K(t.Consumed)} kcal, {K(lo - t.Consumed)} under the range."
                       : $"Logged {K(t.Consumed)} kcal, {K(t.Consumed - hi)} over the range.";
        }
        else
        {
            RangeText = "";
            IsHit = IsOver = IsOff = false;
            RingSweep = 0;
            RingCenter = t is null ? "—" : K(t.Consumed);
            RingCenterSub = t is null ? "no goal yet" : "kcal eaten";
            StatusText = t is null ? "Not logged yet" : "Logged";
            ResultText = t is null ? "" : $"Logged {K(t.Consumed)} kcal. Set a goal to see if it's on target.";
        }
    }

    /// <summary>The last 7 days, today last, with the current goal's target range as a band behind the bars.</summary>
    private void ApplyWeek(CalorieOverview o)
    {
        var today = AppClock.Today;
        var byDate = o.Recent.ToDictionary(d => d.Date);
        var days = Enumerable.Range(0, 7).Select(i => today.AddDays(i - 6)).ToList();
        var top = Math.Max(ChartMinTop, days.Select(d => byDate.TryGetValue(d, out var c) ? c.Consumed : 0).Max());
        double Px(int kcal) => Math.Round(ChartHeight * kcal / top);
        double Share(int kcal) => (double)kcal / top;

        Bars = days.Select(d => byDate.TryGetValue(d, out var c)
                ? new CalorieBarViewModel(d.ToString("ddd", CultureInfo.InvariantCulture), K(c.Consumed),
                    Math.Max(4, Px(c.Consumed)), true, c.Hit == true, d, HasFoodLog && d == today, Share(c.Consumed))
                : new CalorieBarViewModel(d.ToString("ddd", CultureInfo.InvariantCulture), "—", 12, false, false, d, HasFoodLog && d == today, 0.04))
            .ToList();

        var logged = Bars.Count(b => b.IsLogged);
        var hits = Bars.Count(b => b.IsHit);
        OnTargetText = $"{hits} on target";
        HitsText = logged == 0 ? "No days logged yet" : $"{hits} of {logged} logged day{(logged == 1 ? "" : "s")} on target";

        if (o.Goal.Goal is { } goal)
        {
            var (lo, hi) = CalorieGoals.TargetRange(goal);
            BandBottom = Px(lo);
            BandHeight = Px(hi) - Px(lo);
            BandBottomFraction = Share(lo);
            BandHeightFraction = Share(hi) - Share(lo);
        }
        else
        {
            BandBottom = BandHeight = BandBottomFraction = BandHeightFraction = 0;
        }
    }

    private void ApplyMaintenance(ProfileSummary s)
    {
        var m = s.Maintenance;
        HasMaintenance = m.Kcal is not null;
        MaintenanceRowText = m.Kcal is { } mk ? $"≈ {K(mk)} kcal" : "Add body data";
        if (m.Kcal is { } kcal)
        {
            MaintenanceKcal = $"≈ {K(kcal)}";
            TdeeText = K(kcal);
            BmrText = m.Bmr is { } bmr ? K(bmr) : "—";
            ActivityFactorText = m.ActivityFactor?.ToString("0.###", CultureInfo.InvariantCulture) ?? "—";
            MaintenanceNote = s.Profile.ActivityLevel is { } level
                ? $"Mifflin-St Jeor from your body data, with the {ActivityLevels.DisplayName(level)} level ({ActivityLevels.StepRange(level)})."
                : "Mifflin-St Jeor from your body data and activity level.";
        }
        else
        {
            MaintenanceNote = m.Reason ?? "Add your body data on Profile to get an estimate.";
        }
    }

    internal async Task ReloadAsync()
    {
        if (UserId is not { } me) return;
        var o = await _calories.GetAsync(me);
        if (o is null) return;
        ApplyGoal(o.Goal);
        if (TargetsChangedText(o) is { } changed) Info = changed;
        ApplyToday(o);
        ApplyWeek(o);
        await LoadTodayFoodAsync(me);
    }

    // ===== Food (Phase 27: the list itself lives in the Add food sheet) =====

    /// <summary>Today's macros for the bars, and how many items are logged.</summary>
    private async Task LoadTodayFoodAsync(int me)
    {
        if (!HasFoodLog) return;
        var m = await _food.GetDayMacrosAsync(me, AppClock.Today);
        TodayCard.ApplyFood(m, _proteinTarget, _carbsTarget, _fatTarget);
        HasItems = TodayCard.HasItems;
        ProteinBar = TodayCard.ProteinBar;
        CarbsBar = TodayCard.CarbsBar;
        FatBar = TodayCard.FatBar;
    }

    public DateOnly SelectedDate => AppClock.Today;

    /// <summary>A bar of the week: a past day opens its popup, today's does nothing (27d).</summary>
    [RelayCommand]
    private Task ShowDay(DateOnly date) => ShowDayAsync(date);

    internal Task ShowDayAsync(DateOnly date)
    {
        if (!HasFoodLog || date >= AppClock.Today) return Task.CompletedTask;
        if (PastDayOpener is { } open) { open(date); return Task.CompletedTask; }
        return _shell().OpenPastDayAsync(date);
    }

    [RelayCommand] private void AddFoodNow() => _shell().OpenAddFood(null, FoodChangedAsync);

    /// <summary>Manage food: the Add food sheet, scrolled to Logged today when something is logged.</summary>
    [RelayCommand] private void ManageFood() => _shell().OpenAddFood(null, FoodChangedAsync, scrollToLogged: HasItems);

    [RelayCommand] private void OpenGoalSheet() => _shell().OpenCalorieGoalSheet(this);

    /// <summary>The sheet changed today's food: show the points it earned and reload.</summary>
    private async Task FoodChangedAsync(FoodResult r)
    {
        Error = null;
        Info = r.Points is { Added.Count: > 0 } p
            ? string.Join(" · ", p.Added.GroupBy(a => a.Reason).Select(g => $"+{g.Sum(a => a.Amount)} {PointsCalculator.DisplayName(g.Key)}"))
            : null;
        await ReloadAsync();
    }

    // ===== Today's log =====

    [RelayCommand]
    private async Task LogAsync()
    {
        try
        {
            if (UserId is not { } me) return;
            TodayError = null;
            Info = null;
            if (!NumberParsing.TryParseWhole(TodayInput, out var kcal))
            {
                TodayError = "Enter what you ate today as a whole number of kcal, e.g. 2100.";
                return;
            }

            var r = await _calories.LogTodayAsync(me, kcal);
            if (!r.Success) { TodayError = r.Error; return; }

            TodayInput = "";
            IsEditing = false;
            if (r.Points is { Added.Count: > 0 } p)
                Info = string.Join(" · ", p.Added.GroupBy(a => a.Reason)
                    .Select(g => $"+{g.Sum(a => a.Amount)} {PointsCalculator.DisplayName(g.Key)}"));
            await ReloadAsync();
        }
        catch (Exception)
        {
            TodayError = "Could not save your calories.";
        }
    }

    [RelayCommand]
    private void Edit()
    {
        TodayError = null;
        TodayInput = _consumed?.ToString(CultureInfo.InvariantCulture) ?? "";
        IsEditing = true;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        TodayError = null;
        TodayInput = "";
        IsEditing = false;
    }

    // ===== Calorie goal =====

    internal async Task ChangeGoalTypeAsync(GoalType type)
    {
        try
        {
            if (UserId is not { } me) return;
            Info = null;
            var r = await _calories.SetGoalTypeAsync(me, type);
            if (!r.Success) { Error = r.Error; return; }
            Error = null;
            await ReloadAsync();
        }
        catch (Exception)
        {
            Error = "Could not change your goal type.";
        }
    }

    [RelayCommand]
    private async Task UseSuggestionAsync()
    {
        try
        {
            if (UserId is not { } me) return;
            Info = null;
            var r = await _calories.AcceptSuggestionAsync(me);
            if (!r.Success) { Error = r.Error; return; }
            Error = null;
            Info = "Calorie goal updated.";
            await ReloadAsync();
        }
        catch (Exception)
        {
            Error = "Could not save your calorie goal.";
        }
    }

    [RelayCommand]
    private async Task KeepGoalAsync()
    {
        try
        {
            if (UserId is not { } me) return;
            Info = null;
            var r = await _calories.KeepGoalAsync(me);
            if (!r.Success) { Error = r.Error; return; }
            Error = null;
            await ReloadAsync();
        }
        catch (Exception)
        {
            Error = "Could not save your calorie goal.";
        }
    }

    [RelayCommand]
    private async Task SetCustomGoalAsync()
    {
        try
        {
            if (UserId is not { } me) return;
            Info = null;
            Error = null;
            GoalError = null;
            if (!NumberParsing.TryParseWhole(CustomGoalInput, out var kcal))
            {
                GoalError = "Enter your goal as a whole number of kcal, e.g. 2200.";
                return;
            }

            var r = await _calories.SetCustomGoalAsync(me, kcal);
            if (!r.Success) { GoalError = r.Error; return; }
            CustomGoalInput = "";
            Info = "Calorie goal saved.";
            await ReloadAsync();
        }
        catch (Exception)
        {
            GoalError = "Could not save your calorie goal.";
        }
    }

    [RelayCommand] private void ShowProfile() => _shell().ShowProfile(ProfilePage.BodyData);
}
