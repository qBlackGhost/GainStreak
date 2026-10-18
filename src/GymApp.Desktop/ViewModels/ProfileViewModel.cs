using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Services;

namespace GymApp.Desktop.ViewModels;

/// <summary>One row in the activity level picker.</summary>
public record ActivityOption(ActivityLevel Level)
{
    public string Name => ActivityLevels.DisplayName(Level);
    public string Range => ActivityLevels.StepRange(Level);
}

/// <summary>One row in the goal picker.</summary>
public record GoalOption(GoalType Type)
{
    public string Name => CalorieGoals.DisplayName(Type);
}

/// <summary>One logged day in the calorie history.</summary>
public record CalorieDayRow(string DateText, string ConsumedText, string ResultText);

/// <summary>One weigh-in in the history list.</summary>
public partial class WeightEntryViewModel : ObservableObject
{
    private readonly ProfileViewModel _owner;

    public WeightEntryViewModel(WeightLog entry, UnitSystem units, ProfileViewModel owner)
    {
        Entry = entry;
        _owner = owner;
        DateText = entry.Date.ToString("ddd d MMM yyyy", CultureInfo.InvariantCulture);
        WeightText = Units.FormatWeight(entry.WeightKg, units);
    }

    public WeightLog Entry { get; }
    public string DateText { get; }
    public string WeightText { get; }

    [RelayCommand] private Task Delete() => _owner.DeleteWeightAsync(this);
}

public partial class ProfileViewModel : ViewModelBase
{
    private readonly ProfileService _profiles;
    private readonly CalorieService _calories;
    private readonly GeocodingService _geocoding;
    private readonly CityService _cities;
    private readonly SessionService _session;
    private readonly Func<MainViewModel> _shell;

    public ProfileViewModel(ProfileService profiles, CalorieService calories, GeocodingService geocoding,
        CityService cities, SessionService session, Func<MainViewModel> shell)
    {
        _profiles = profiles; _calories = calories; _geocoding = geocoding; _cities = cities;
        _session = session; _shell = shell;
        _ = ReloadAsync();
    }

    private int UserId => _session.CurrentUser!.Id;
    private static DateOnly Today => AppClock.Today;

    public IReadOnlyList<Sex> Sexes { get; } = Enum.GetValues<Sex>();
    public IReadOnlyList<UnitSystem> UnitSystems { get; } = Enum.GetValues<UnitSystem>();
    public IReadOnlyList<ActivityOption> ActivityOptions { get; } =
        Array.ConvertAll(Enum.GetValues<ActivityLevel>(), l => new ActivityOption(l));
    public ObservableCollection<WeightEntryViewModel> History { get; } = new();
    public IReadOnlyList<GoalOption> GoalOptions { get; } =
        Array.ConvertAll(Enum.GetValues<GoalType>(), g => new GoalOption(g));
    public ObservableCollection<CalorieDayRow> CalorieHistory { get; } = new();
    public ObservableCollection<CityCandidateViewModel> CityCandidates { get; } = new();

    // The birth date picker only offers years that pass validation
    public DateTimeOffset MinBirthYear { get; } = new(AppClock.Today.Year - BodyLimits.MaxAge, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public DateTimeOffset MaxBirthYear { get; } = new(AppClock.Today.Year - BodyLimits.MinAge, 12, 31, 0, 0, 0, TimeSpan.Zero);

    // ===== Header =====
    [ObservableProperty] public partial string Username { get; set; } = "";
    [ObservableProperty] public partial string SplitText { get; set; } = "";
    [ObservableProperty] public partial string StreakText { get; set; } = "";

    // ===== Body data form =====
    [ObservableProperty] public partial Sex? SelectedSex { get; set; }
    [ObservableProperty] public partial DateTimeOffset? BirthDate { get; set; }
    [ObservableProperty] public partial string HeightCmText { get; set; } = "";
    [ObservableProperty] public partial string HeightFtText { get; set; } = "";
    [ObservableProperty] public partial string HeightInText { get; set; } = "";
    [ObservableProperty] public partial ActivityOption? SelectedActivity { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMetric), nameof(IsImperial), nameof(WeightWatermark))]
    public partial UnitSystem SelectedUnits { get; set; } = UnitSystem.Metric;

    public bool IsMetric => SelectedUnits == UnitSystem.Metric;
    public bool IsImperial => SelectedUnits == UnitSystem.Imperial;
    public string WeightWatermark => IsMetric ? "Weight (kg)" : "Weight (lb)";

    // ===== Weight =====
    [ObservableProperty] public partial string WeightInput { get; set; } = "";
    [ObservableProperty] public partial string CurrentWeightText { get; set; } = "";
    [ObservableProperty] public partial bool HasHistory { get; set; }

    // ===== Maintenance =====
    [ObservableProperty] public partial string MaintenanceText { get; set; } = "";
    [ObservableProperty] public partial string MaintenanceNote { get; set; } = "";
    [ObservableProperty] public partial bool HasMaintenance { get; set; }

    // ===== Calorie goal =====
    [ObservableProperty] public partial GoalOption? SelectedGoal { get; set; }
    [ObservableProperty] public partial string GoalText { get; set; } = "";
    [ObservableProperty] public partial string GoalRule { get; set; } = "";
    [ObservableProperty] public partial string SuggestionText { get; set; } = "";
    [ObservableProperty] public partial string SuggestionNote { get; set; } = "";
    [ObservableProperty] public partial bool CanUseSuggestion { get; set; }
    [ObservableProperty] public partial bool SuggestionChanged { get; set; }
    [ObservableProperty] public partial string SuggestionChangedText { get; set; } = "";
    [ObservableProperty] public partial string UseSuggestionLabel { get; set; } = "Use suggestion";
    [ObservableProperty] public partial string KeepGoalLabel { get; set; } = "Keep my goal";
    [ObservableProperty] public partial string CustomGoalInput { get; set; } = "";
    [ObservableProperty] public partial bool HasCalorieHistory { get; set; }

    // ===== Home city =====
    [ObservableProperty] public partial string HomeCityText { get; set; } = "";
    [ObservableProperty] public partial bool HasHomeCity { get; set; }
    [ObservableProperty] public partial string CitySearchText { get; set; } = "";
    [ObservableProperty] public partial string? CitySearchMessage { get; set; }
    [ObservableProperty] public partial bool CanRetryCitySearch { get; set; }
    [ObservableProperty] public partial bool HasCityCandidates { get; set; }
    [ObservableProperty] public partial bool AskForGym { get; set; } = true;

    /// <summary>"GymApp 1.0.0", from the assembly version (Directory.Build.props).</summary>
    public string VersionText { get; } = $"GymApp {AppVersion.Current}";

    [ObservableProperty] public partial string? Error { get; set; }

    // Messages shown under the field they're about
    [ObservableProperty] public partial string? UnitsError { get; set; }
    [ObservableProperty] public partial string? SexError { get; set; }
    [ObservableProperty] public partial string? BirthDateError { get; set; }
    [ObservableProperty] public partial string? HeightError { get; set; }
    [ObservableProperty] public partial string? ActivityError { get; set; }
    [ObservableProperty] public partial string? WeightError { get; set; }
    [ObservableProperty] public partial string? GoalError { get; set; }

    private void ClearBodyErrors() => UnitsError = SexError = BirthDateError = HeightError = ActivityError = null;
    [ObservableProperty] public partial string? Info { get; set; }

    private bool _loading;

    private async Task ReloadAsync(bool refillForm = true)
    {
        try
        {
            var s = await _profiles.GetAsync(UserId);
            if (s is null) { Error = "Could not load your profile."; return; }

            Username = s.Username;
            SplitText = s.SplitName ?? "No split selected";
            StreakText = $"{s.CurrentStreak} day streak · best {s.LongestStreak}";

            if (refillForm) FillForm(s.Profile);
            ApplyHomeCity(s.Profile.HomeCity);
            _applyingAskForGym = true;   // so filling the toggle does not save it again
            AskForGym = s.Profile.AskForGymAfterCheckIn;
            _applyingAskForGym = false;

            // Weight and history are shown in the saved unit, so they match what the form says after a reload
            var units = s.Profile.Units;
            CurrentWeightText = s.CurrentWeight is { } w
                ? $"{Units.FormatWeight(w.WeightKg, units)} ({DayLabel(w.Date)})"
                : "No weight logged yet";
            History.Clear();
            foreach (var entry in s.RecentWeights) History.Add(new WeightEntryViewModel(entry, units, this));
            HasHistory = History.Count > 0;

            ApplyMaintenance(s.Maintenance);
            await ReloadGoalAsync();
        }
        catch (Exception)
        {
            Error = "Could not load your profile.";
        }
    }

    private void FillForm(UserProfile p)
    {
        _loading = true;   // so switching units while filling does not convert the boxes
        SelectedUnits = p.Units;
        SelectedSex = p.Sex;
        BirthDate = p.BirthDate is { } b ? new DateTimeOffset(b.ToDateTime(TimeOnly.MinValue)) : null;
        SetHeightFields(p.HeightCm, p.Units);
        SelectedActivity = p.ActivityLevel is { } a ? new ActivityOption(a) : null;
        _loading = false;
    }

    private void ApplyMaintenance(MaintenanceEstimate m)
    {
        HasMaintenance = m.Kcal is not null;
        if (m.Kcal is { } kcal)
        {
            MaintenanceText = $"≈ {kcal.ToString("N0", CultureInfo.InvariantCulture)} kcal / day";
            MaintenanceNote = $"Estimate: BMR {m.Bmr?.ToString("N0", CultureInfo.InvariantCulture)} kcal " +
                              $"x activity {m.ActivityFactor?.ToString("0.###", CultureInfo.InvariantCulture)} " +
                              "(Mifflin-St Jeor, from your activity level).";
        }
        else
        {
            MaintenanceText = "";
            MaintenanceNote = m.Reason ?? "";
        }
    }

    // Switching units converts what is already typed, so nothing is lost
    partial void OnSelectedUnitsChanged(UnitSystem oldValue, UnitSystem newValue)
    {
        if (_loading || oldValue == newValue) return;

        if (TryReadHeight(oldValue, out var cm, out _)) SetHeightFields(cm, newValue);
        if (Units.TryParseNumber(WeightInput, out var w))
        {
            var kg = oldValue == UnitSystem.Metric ? w : Units.LbToKg(w);
            WeightInput = Units.WeightInputText(kg, newValue);
        }
    }

    private void SetHeightFields(double? cm, UnitSystem units)
    {
        HeightCmText = HeightFtText = HeightInText = "";
        if (cm is not { } h) return;
        if (units == UnitSystem.Metric)
            HeightCmText = Math.Round(h, 1).ToString("0.#", CultureInfo.InvariantCulture);
        else
        {
            var (ft, inch) = Units.CmToFeetInches(h);
            HeightFtText = ft.ToString(CultureInfo.InvariantCulture);
            HeightInText = inch.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>False with an error when something was typed but is not a number. Empty boxes are fine (cm = null).</summary>
    private bool TryReadHeight(UnitSystem units, out double? cm, out string? error)
    {
        cm = null; error = null;
        if (units == UnitSystem.Metric)
        {
            if (string.IsNullOrWhiteSpace(HeightCmText)) return true;
            if (!Units.TryParseNumber(HeightCmText, out var v)) { error = "Height must be a number in cm."; return false; }
            cm = v;
            return true;
        }

        if (string.IsNullOrWhiteSpace(HeightFtText) && string.IsNullOrWhiteSpace(HeightInText)) return true;
        if (!NumberParsing.TryParseWhole(HeightFtText, out var ft))
        { error = "Enter your height in whole feet, e.g. 5."; return false; }

        double inches = 0;
        if (!string.IsNullOrWhiteSpace(HeightInText) && (!Units.TryParseNumber(HeightInText, out inches) || inches >= 12))
        { error = "Inches must be a number from 0 to 11."; return false; }

        cm = Units.FeetInchesToCm(ft, inches);
        return true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            Info = null;
            Error = null;
            ClearBodyErrors();
            if (!TryReadHeight(SelectedUnits, out var cm, out var heightError)) { HeightError = heightError; return; }

            DateOnly? birth = BirthDate is { } b ? DateOnly.FromDateTime(b.Date) : null;

            var r = await _profiles.SaveAsync(UserId, new ProfileInput(SelectedSex, birth, cm, SelectedActivity?.Level, SelectedUnits));
            if (!r.Success)
            {
                switch (r.Field)
                {
                    case FormField.Units: UnitsError = r.Error; break;
                    case FormField.Sex: SexError = r.Error; break;
                    case FormField.BirthDate: BirthDateError = r.Error; break;
                    case FormField.Height: HeightError = r.Error; break;
                    case FormField.ActivityLevel: ActivityError = r.Error; break;
                    default: Error = r.Error; break;
                }
                return;
            }

            Error = null;
            Info = "Profile saved.";
            await ReloadAsync();

        }
        catch (Exception)
        {
            Error = "Could not save your profile.";
        }
    }

    [RelayCommand]
    private async Task LogWeightAsync()
    {
        try
        {
            Info = null;
            Error = null;
            WeightError = null;
            if (!Units.TryParseNumber(WeightInput, out var value))
            {
                WeightError = IsMetric ? "Enter your weight in kg, e.g. 80.5." : "Enter your weight in lb, e.g. 177.";
                return;
            }

            var kg = IsMetric ? value : Units.LbToKg(value);
            var r = await _profiles.LogWeightAsync(UserId, kg);
            if (!r.Success) { WeightError = r.Error; return; }

            WeightInput = "";
            Info = "Weight saved for today.";
            await ReloadAsync(refillForm: false);   // keep any unsaved edits in the form

        }
        catch (Exception)
        {
            WeightError = "Could not save your weight.";
        }
    }

    internal async Task DeleteWeightAsync(WeightEntryViewModel item)
    {
        try
        {
            if (!await _profiles.DeleteWeightAsync(UserId, item.Entry.Id))
            {
                Error = "Could not delete that weigh-in.";
                return;
            }
            Error = null;
            Info = "Weigh-in deleted.";
            await ReloadAsync(refillForm: false);

        }
        catch (Exception)
        {
            Error = "Could not delete that weigh-in.";
        }
    }

    // ===== Gym prompt =====

    private bool _applyingAskForGym;

    // Saved straight away, like the goal picker
    partial void OnAskForGymChanged(bool value)
    {
        if (_applyingAskForGym) return;
        _ = SaveAskForGymAsync(value);
    }

    private async Task SaveAskForGymAsync(bool value)
    {
        try
        {
            Info = null;
            var r = await _profiles.SetAskForGymAfterCheckInAsync(UserId, value);
            if (!r.Success) { Error = r.Error; return; }
            Error = null;
            Info = value ? "We'll ask which gym after you check in." : "We won't ask which gym after check-in.";

        }
        catch (Exception)
        {
            Error = "Could not save that setting.";
        }
    }

    // ===== Home city =====

    private void ApplyHomeCity(City? city)
    {
        HasHomeCity = city is not null;
        HomeCityText = city?.DisplayName ?? "Not set. The gym picker will ask for a city.";
    }

    // Runs on the Search button or Enter, never per keystroke: Nominatim allows one request per second
    [RelayCommand]
    private async Task SearchCityAsync()
    {
        try
        {
            Info = null;
            CitySearchMessage = null;
            CanRetryCitySearch = false;
            CityCandidates.Clear();
            HasCityCandidates = false;

            var query = CitySearchText.Trim();
            var r = await _geocoding.SearchAsync(query);
            if (!r.Success) { CitySearchMessage = r.Error; CanRetryCitySearch = r.CanRetry; return; }
            if (r.Candidates.Count == 0) { CitySearchMessage = $"No cities found for \"{query}\"."; return; }

            foreach (var c in r.Candidates) CityCandidates.Add(new CityCandidateViewModel(c, ChooseHomeCityAsync));
            HasCityCandidates = true;

        }
        catch (Exception)
        {
            CitySearchMessage = "Could not search for cities.";
        }
    }

    private async Task ChooseHomeCityAsync(CityCandidate candidate)
    {
        Info = null;
        try
        {
            var city = await _cities.UpsertAsync(candidate);
            var r = await _profiles.SetHomeCityAsync(UserId, city.Id);
            if (!r.Success) { Error = r.Error; return; }
            ApplyHomeCity(city);
        }
        catch (Exception)
        {
            Error = "Could not save your home city.";
            return;
        }

        Error = null;
        CityCandidates.Clear();
        HasCityCandidates = false;
        CitySearchText = "";
        CitySearchMessage = null;
        Info = $"Home city set to {candidate.Name}.";
    }

    [RelayCommand]
    private async Task ClearHomeCityAsync()
    {
        try
        {
            Info = null;
            var r = await _profiles.SetHomeCityAsync(UserId, null);
            if (!r.Success) { Error = r.Error; return; }
            Error = null;
            ApplyHomeCity(null);
            Info = "Home city cleared.";

        }
        catch (Exception)
        {
            Error = "Could not clear your home city.";
        }
    }

    // ===== Calorie goal =====

    private bool _applyingGoal;

    private async Task ReloadGoalAsync()
    {
        try
        {
            var o = await _calories.GetAsync(UserId);
            if (o is null) return;
            var g = o.Goal;

            _applyingGoal = true;   // so setting the picker does not save it again
            SelectedGoal = new GoalOption(g.GoalType);
            _applyingGoal = false;

            GoalText = g.Goal is { } goal ? $"{CalorieGoals.K(goal)} kcal / day" : "No goal set yet";
            GoalRule = g.Goal is { } target ? $"A day counts as hit when you eat {CalorieGoals.TargetRule(target)}." : "";

            if (g.Suggestion is { } s)
            {
                SuggestionText = $"Suggested for {CalorieGoals.DisplayName(g.GoalType).ToLowerInvariant()}: {CalorieGoals.K(s.Kcal)} kcal / day";
                SuggestionNote = s.Note ?? "An estimate from your maintenance calories.";
                UseSuggestionLabel = $"Use {CalorieGoals.K(s.Kcal)} kcal";
            }
            else
            {
                SuggestionText = "";
                SuggestionNote = g.SuggestionReason ?? "";
            }
            CanUseSuggestion = g.Suggestion is { } sug && sug.Kcal != g.Goal && !g.SuggestionChanged;

            SuggestionChanged = g.SuggestionChanged;
            if (g.SuggestionChanged && g.Goal is { } current && g.Suggestion is { } next)
            {
                SuggestionChangedText = $"Your suggested goal is now {CalorieGoals.K(next.Kcal)} kcal. " +
                                        $"Your goal is still {CalorieGoals.K(current)} kcal. Update it?";
                KeepGoalLabel = $"Keep {CalorieGoals.K(current)} kcal";
            }

            CalorieHistory.Clear();
            foreach (var d in o.Recent)
                CalorieHistory.Add(new CalorieDayRow(
                    d.Date.ToString("ddd d MMM", CultureInfo.InvariantCulture),
                    $"{CalorieGoals.K(d.Consumed)} kcal",
                    d.Hit switch
                    {
                        true => $"hit ✓ (goal {CalorieGoals.K(d.Goal!.Value)})",
                        false => $"missed (goal {CalorieGoals.K(d.Goal!.Value)})",
                        null => "no goal"
                    }));
            HasCalorieHistory = CalorieHistory.Count > 0;

        }
        catch (Exception)
        {
            Error = "Could not load your calorie goal.";
        }
    }

    partial void OnSelectedGoalChanged(GoalOption? value)
    {
        if (_applyingGoal || value is null) return;
        _ = ChangeGoalTypeAsync(value.Type);
    }

    private async Task ChangeGoalTypeAsync(GoalType type)
    {
        try
        {
            Info = null;
            var r = await _calories.SetGoalTypeAsync(UserId, type);
            if (!r.Success) { Error = r.Error; return; }
            Error = null;
            await ReloadGoalAsync();

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
            Info = null;
            var r = await _calories.AcceptSuggestionAsync(UserId);
            if (!r.Success) { Error = r.Error; return; }
            Error = null;
            Info = "Calorie goal updated.";
            await ReloadGoalAsync();

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
            Info = null;
            var r = await _calories.KeepGoalAsync(UserId);
            if (!r.Success) { Error = r.Error; return; }
            Error = null;
            await ReloadGoalAsync();

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
            Info = null;
            Error = null;
            GoalError = null;
            if (!NumberParsing.TryParseWhole(CustomGoalInput, out var kcal))
            {
                GoalError = "Enter your goal as a whole number of kcal, e.g. 2200.";
                return;
            }

            var r = await _calories.SetCustomGoalAsync(UserId, kcal);
            if (!r.Success) { GoalError = r.Error; return; }
            CustomGoalInput = "";
            Info = "Calorie goal saved.";
            await ReloadGoalAsync();

        }
        catch (Exception)
        {
            GoalError = "Could not save your calorie goal.";
        }
    }

    [RelayCommand]
    private void Back() => _shell().ShowHome();

    [RelayCommand]
    private void ShowGymSummary() => _shell().ShowGymSummary(fromProfile: true);

    private static string DayLabel(DateOnly date)
    {
        var days = Today.DayNumber - date.DayNumber;
        return days switch
        {
            0 => "today",
            1 => "yesterday",
            _ => date.ToString("d MMM", CultureInfo.InvariantCulture)
        };
    }
}
