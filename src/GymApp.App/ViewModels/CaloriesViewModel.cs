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
public record CalorieBarViewModel(string Day, string Label, double BarHeight, bool IsLogged, bool IsHit)
{
    public bool IsOff => IsLogged && !IsHit;
    public bool IsMissing => !IsLogged;
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

    public CaloriesViewModel(CalorieService calories, ProfileService profiles, SessionService session, Func<MainViewModel> shell)
    {
        _calories = calories;
        _profiles = profiles;
        _session = session;
        _shell = shell;
        GoalTypes = Enum.GetValues<GoalType>().Select(t => new GoalTypeOptionViewModel(t, this)).ToList();
        _ = LoadAsync();
    }

    private int? UserId => _session.CurrentUser?.Id;
    private static string K(int kcal) => CalorieGoals.K(kcal);

    public string DateText => DateTime.Now.ToString("dddd, d MMMM", CultureInfo.InvariantCulture);
    public IReadOnlyList<GoalTypeOptionViewModel> GoalTypes { get; }

    /// <summary>The selected goal type's position in <see cref="GoalTypes"/>, for the sliding pill.</summary>
    [ObservableProperty] public partial int SelectedGoalIndex { get; set; }

    [ObservableProperty] public partial string? Error { get; set; }
    [ObservableProperty] public partial string? Info { get; set; }
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
    [NotifyPropertyChangedFor(nameof(ShowLogForm))]
    public partial bool IsLogged { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLogForm))]
    public partial bool IsEditing { get; set; }

    public bool ShowLogForm => !IsLogged || IsEditing;

    // ===== Last 7 days =====
    [ObservableProperty] public partial IReadOnlyList<CalorieBarViewModel> Bars { get; set; } = [];
    [ObservableProperty] public partial string HitsText { get; set; } = "";
    [ObservableProperty] public partial double BandBottom { get; set; }
    [ObservableProperty] public partial double BandHeight { get; set; }

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

    // ===== Maintenance =====
    [ObservableProperty] public partial bool HasMaintenance { get; set; }
    [ObservableProperty] public partial string MaintenanceKcal { get; set; } = "";
    [ObservableProperty] public partial string TdeeText { get; set; } = "";
    [ObservableProperty] public partial string BmrText { get; set; } = "";
    [ObservableProperty] public partial string ActivityFactorText { get; set; } = "";
    [ObservableProperty] public partial string MaintenanceNote { get; set; } = "";

    private int? _consumed;

    private async Task LoadAsync()
    {
        if (UserId is not { } me) return;
        try
        {
            var o = await _calories.GetAsync(me);
            if (o is null) { Error = "Could not load your calories."; return; }
            ApplyGoal(o.Goal);
            ApplyToday(o);
            ApplyWeek(o);
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

    private void ApplyGoal(CalorieGoalInfo g)
    {
        foreach (var t in GoalTypes) t.IsSelected = t.Type == g.GoalType;
        SelectedGoalIndex = GoalTypes.ToList().FindIndex(t => t.Type == g.GoalType);
        var type = CalorieGoals.DisplayName(g.GoalType);

        HasGoal = g.Goal is not null;
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
        GoalKcal = goal is { } gk ? K(gk) : "—";
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

        Bars = days.Select(d => byDate.TryGetValue(d, out var c)
                ? new CalorieBarViewModel(d.ToString("ddd", CultureInfo.InvariantCulture), K(c.Consumed),
                    Math.Max(4, Px(c.Consumed)), true, c.Hit == true)
                : new CalorieBarViewModel(d.ToString("ddd", CultureInfo.InvariantCulture), "—", 12, false, false))
            .ToList();

        var logged = Bars.Count(b => b.IsLogged);
        var hits = Bars.Count(b => b.IsHit);
        HitsText = logged == 0 ? "No days logged yet" : $"{hits} of {logged} logged day{(logged == 1 ? "" : "s")} on target";

        if (o.Goal.Goal is { } goal)
        {
            var (lo, hi) = CalorieGoals.TargetRange(goal);
            BandBottom = Px(lo);
            BandHeight = Px(hi) - Px(lo);
        }
        else
        {
            BandBottom = BandHeight = 0;
        }
    }

    private void ApplyMaintenance(ProfileSummary s)
    {
        var m = s.Maintenance;
        HasMaintenance = m.Kcal is not null;
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

    private async Task ReloadAsync()
    {
        if (UserId is not { } me) return;
        var o = await _calories.GetAsync(me);
        if (o is null) return;
        ApplyGoal(o.Goal);
        ApplyToday(o);
        ApplyWeek(o);
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
