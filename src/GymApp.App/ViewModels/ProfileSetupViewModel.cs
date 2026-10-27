using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Services;

namespace GymApp.App.ViewModels;

/// <summary>One line of the "You're all set" summary: "Height", "180 cm", or "Not set".</summary>
public record SetupSummaryRow(string Label, string Value, bool IsSet, bool IsFirst);

/// <summary>
/// The profile setup a new account sees right after signing up (design canvas: Profile setup). Every field is optional;
/// nothing is saved until "Save and continue", and "Skip for now" saves nothing. It fills the same profile, weigh-in
/// and home city as the Profile screen, where all of it can be changed later.
/// </summary>
public partial class ProfileSetupViewModel : ViewModelBase
{
    /// <summary>The gender options, in order: the calorie formula only knows the first two.</summary>
    public const int Male = 0, Female = 1, PreferNotToSay = 2;

    private readonly ProfileService _profiles;
    private readonly CalorieService _calories;
    private readonly GeocodingService _geocoding;
    private readonly CityService _cities;
    private readonly SessionService _session;
    private readonly Func<MainViewModel> _shell;

    public ProfileSetupViewModel(ProfileService profiles, CalorieService calories, GeocodingService geocoding,
        CityService cities, SessionService session, Func<MainViewModel> shell)
    {
        _profiles = profiles; _calories = calories; _geocoding = geocoding; _cities = cities;
        _session = session; _shell = shell;
        ActivityOptions = Enum.GetValues<ActivityLevel>().Select(l => new ActivityOption(l, o => SelectedActivity = o)).ToList();
        Username = session.CurrentUser?.Username ?? "";
        Recompute();
    }

    private int UserId => _session.CurrentUser!.Id;
    private static DateOnly Today => AppClock.Today;

    public string Username { get; }
    public string WelcomeText => $"WELCOME, {Username.ToUpperInvariant()}";

    public IReadOnlyList<ActivityOption> ActivityOptions { get; }
    public IReadOnlyList<MonthOption> Months { get; } = Enumerable.Range(1, 12)
        .Select(m => new MonthOption(m, CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(m))).ToList();
    public ObservableCollection<CityCandidateViewModel> CityCandidates { get; } = new();

    // ===== The form =====
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMetric), nameof(IsImperial), nameof(UnitsIndex), nameof(WeightUnit), nameof(WeightWatermark))]
    public partial UnitSystem SelectedUnits { get; set; } = UnitSystem.Metric;

    /// <summary>-1 until picked; <see cref="PreferNotToSay"/> is an answer, saved as no gender.</summary>
    [ObservableProperty] public partial int GenderIndex { get; set; } = -1;

    [ObservableProperty] public partial string BirthDayText { get; set; } = "";
    [ObservableProperty] public partial MonthOption? BirthMonth { get; set; }
    [ObservableProperty] public partial string BirthYearText { get; set; } = "";
    [ObservableProperty] public partial string HeightCmText { get; set; } = "";
    [ObservableProperty] public partial string HeightFtText { get; set; } = "";
    [ObservableProperty] public partial string HeightInText { get; set; } = "";
    [ObservableProperty] public partial string WeightText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActivityIndex))]
    public partial ActivityOption? SelectedActivity { get; set; }

    public bool IsMetric => SelectedUnits == UnitSystem.Metric;
    public bool IsImperial => SelectedUnits == UnitSystem.Imperial;
    public int UnitsIndex => IsMetric ? 0 : 1;
    public int ActivityIndex => SelectedActivity is { } a ? ActivityOptions.ToList().FindIndex(o => o.Level == a.Level) : -1;
    public string WeightUnit => IsMetric ? "kg" : "lb";
    public string WeightWatermark => IsMetric ? "e.g. 72.5" : "e.g. 160";

    [RelayCommand] private void SelectMetric() => SelectedUnits = UnitSystem.Metric;
    [RelayCommand] private void SelectImperial() => SelectedUnits = UnitSystem.Imperial;
    [RelayCommand] private void SelectGender(string index) => GenderIndex = int.Parse(index, CultureInfo.InvariantCulture);

    // ===== Worked out from the form (Recompute) =====
    [ObservableProperty] public partial string? AgeText { get; set; }
    [ObservableProperty] public partial string? BirthDateError { get; set; }
    [ObservableProperty] public partial string? HeightError { get; set; }
    [ObservableProperty] public partial string? WeightError { get; set; }

    [ObservableProperty] public partial bool HasEstimate { get; set; }
    [ObservableProperty] public partial string EstimateText { get; set; } = "";
    [ObservableProperty] public partial string EstimateMessage { get; set; } = "";
    [ObservableProperty] public partial bool EstimateIsWarning { get; set; }
    [ObservableProperty] public partial string FilledText { get; set; } = "0 / 5";
    [ObservableProperty] public partial IReadOnlyList<bool> Progress { get; set; } = [false, false, false, false, false];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool CanSave { get; set; }
    [ObservableProperty] public partial string? SaveHint { get; set; }
    [ObservableProperty] public partial bool SaveHintIsError { get; set; }

    // ===== Home city: picked here, saved with the rest =====
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCity))]
    public partial CityCandidate? ChosenCity { get; set; }
    public bool HasCity => ChosenCity is not null;
    public string CityName => ChosenCity?.Name ?? "";
    public string CityRegion => ChosenCity?.DisplayName.Split(", ", 2) is [_, var region] ? region : "";
    [ObservableProperty] public partial string CitySearchText { get; set; } = "";
    [ObservableProperty] public partial string? CitySearchMessage { get; set; }
    [ObservableProperty] public partial bool CanRetryCitySearch { get; set; }
    [ObservableProperty] public partial bool HasCityCandidates { get; set; }

    // ===== After saving =====
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    public partial bool IsDone { get; set; }
    public bool IsEditing => !IsDone;
    [ObservableProperty] public partial IReadOnlyList<SetupSummaryRow> Summary { get; set; } = [];
    [ObservableProperty] public partial bool HasSavedEstimate { get; set; }
    [ObservableProperty] public partial string SavedEstimateText { get; set; } = "";
    [ObservableProperty] public partial string SavedEstimateMessage { get; set; } = "";
    [ObservableProperty] public partial bool IsGoalSet { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(UseAsGoalCommand))]
    public partial bool IsBusy { get; set; }

    private bool _converting;

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_converting) return;
        if (e.PropertyName is nameof(SelectedUnits) or nameof(GenderIndex) or nameof(BirthDayText) or nameof(BirthMonth)
            or nameof(BirthYearText) or nameof(HeightCmText) or nameof(HeightFtText) or nameof(HeightInText)
            or nameof(WeightText) or nameof(SelectedActivity) or nameof(ChosenCity))
            Recompute();
    }

    partial void OnSelectedActivityChanged(ActivityOption? value)
    {
        foreach (var o in ActivityOptions) o.IsSelected = o == value;
    }

    partial void OnChosenCityChanged(CityCandidate? value)
    {
        OnPropertyChanged(nameof(CityName));
        OnPropertyChanged(nameof(CityRegion));
    }

    // Switching units converts what is already typed, so nothing is lost
    partial void OnSelectedUnitsChanged(UnitSystem oldValue, UnitSystem newValue)
    {
        if (oldValue == newValue) return;
        _converting = true;
        if (ReadHeight(oldValue) is { Cm: { } cm }) SetHeightFields(cm, newValue);
        if (Units.TryParseNumber(WeightText, out var w))
            WeightText = Units.WeightInputText(oldValue == UnitSystem.Metric ? w : Units.LbToKg(w), newValue);
        _converting = false;
    }

    private void SetHeightFields(double cm, UnitSystem units)
    {
        HeightCmText = HeightFtText = HeightInText = "";
        if (units == UnitSystem.Metric)
            HeightCmText = Math.Round(cm).ToString(CultureInfo.InvariantCulture);
        else
        {
            var (ft, inch) = Units.CmToFeetInches(cm);
            HeightFtText = ft.ToString(CultureInfo.InvariantCulture);
            HeightInText = inch.ToString(CultureInfo.InvariantCulture);
        }
    }

    private bool BirthDateEmpty =>
        string.IsNullOrWhiteSpace(BirthDayText) && BirthMonth is null && string.IsNullOrWhiteSpace(BirthYearText);

    /// <summary>The birth date once day, month and a four-digit year are in; an error only for something wrong,
    /// not for a date still being typed.</summary>
    private (DateOnly? Date, string? Error) ReadBirthDate()
    {
        if (BirthDateEmpty) return (null, null);
        var dayTyped = !string.IsNullOrWhiteSpace(BirthDayText);
        if (dayTyped && (!NumberParsing.TryParseWhole(BirthDayText, out var d0) || d0 is < 1 or > 31))
            return (null, "Enter a day from 1 to 31.");
        if (!dayTyped || BirthMonth is not { } m || BirthYearText.Trim().Length < 4) return (null, null);
        if (!NumberParsing.TryParseWhole(BirthYearText, out var y) || y is < 1 or > 9999)
            return (null, "Enter the year with four digits, e.g. 1998.");
        NumberParsing.TryParseWhole(BirthDayText, out var d);
        if (d > DateTime.DaysInMonth(y, m.Number)) return (null, $"{m.Name} doesn't have {d} days.");
        var date = new DateOnly(y, m.Number, d);
        return BodyMetrics.CheckBirthDate(date, Today) is { } e ? (null, e) : (date, null);
    }

    private (double? Cm, string? Error) ReadHeight(UnitSystem units)
    {
        if (units == UnitSystem.Metric)
        {
            if (string.IsNullOrWhiteSpace(HeightCmText)) return (null, null);
            if (!Units.TryParseNumber(HeightCmText, out var v)) return (null, "Height must be a number in cm.");
            return BodyMetrics.CheckHeight(v) is { } e ? (null, e) : (v, null);
        }

        if (string.IsNullOrWhiteSpace(HeightFtText) && string.IsNullOrWhiteSpace(HeightInText)) return (null, null);
        if (!NumberParsing.TryParseWhole(HeightFtText, out var ft)) return (null, "Enter your height in whole feet, e.g. 5.");
        double inches = 0;
        if (!string.IsNullOrWhiteSpace(HeightInText) && (!Units.TryParseNumber(HeightInText, out inches) || inches is < 0 or >= 12))
            return (null, "Inches must be a number from 0 to 11.");
        var cm = Units.FeetInchesToCm(ft, inches);
        return BodyMetrics.CheckHeight(cm) is null ? (cm, null) : (null, "Height must be between 3 ft 3 in and 8 ft 2 in.");
    }

    private (double? Kg, string? Error) ReadWeight()
    {
        if (string.IsNullOrWhiteSpace(WeightText)) return (null, null);
        if (!Units.TryParseNumber(WeightText, out var v)) return (null, $"Weight must be a number in {WeightUnit}.");
        var kg = IsMetric ? v : Units.LbToKg(v);
        if (BodyMetrics.CheckWeight(kg) is null) return (kg, null);
        return (null, IsMetric
            ? $"Enter a weight between {BodyLimits.MinWeightKg:0} and {BodyLimits.MaxWeightKg:0} kg."
            : $"Enter a weight between {Units.KgToLb(BodyLimits.MinWeightKg):0} and {Units.KgToLb(BodyLimits.MaxWeightKg):0} lb.");
    }

    private Sex? GenderAsSex => GenderIndex switch { Male => Sex.Male, Female => Sex.Female, _ => null };

    private static string JoinList(List<string> items) => items.Count switch
    {
        1 => items[0],
        2 => $"{items[0]} and {items[1]}",
        _ => $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}"
    };

    /// <summary>Errors, age, the live estimate and whether Save is allowed, from what is typed now.</summary>
    private void Recompute()
    {
        var birth = ReadBirthDate();
        var height = ReadHeight(SelectedUnits);
        var weight = ReadWeight();
        BirthDateError = birth.Error;
        HeightError = height.Error;
        WeightError = weight.Error;
        var age = birth.Date is { } b ? BodyMetrics.AgeOn(b, Today) : (int?)null;
        AgeText = age is { } a ? $"Age {a}" : null;

        bool[] answered = [GenderIndex >= 0, age is not null, height.Cm is not null, weight.Kg is not null, SelectedActivity is not null];
        Progress = answered;
        FilledText = $"{answered.Count(x => x)} / 5";

        var hasErrors = birth.Error is not null || height.Error is not null || weight.Error is not null;
        var missing = new List<string>();
        if (!answered[0]) missing.Add("gender");
        if (!answered[1]) missing.Add("birth date");
        if (!answered[2]) missing.Add("height");
        if (!answered[3]) missing.Add("weight");
        if (!answered[4]) missing.Add("activity");

        var estimate = ProfileService.Estimate(new UserProfile
        {
            Sex = GenderAsSex, BirthDate = birth.Date, HeightCm = height.Cm, ActivityLevel = SelectedActivity?.Level
        }, weight.Kg, Today);
        HasEstimate = estimate.Kcal is not null;
        EstimateText = estimate.Kcal is { } kcal ? $"≈ {kcal.ToString("N0", CultureInfo.InvariantCulture)}" : "";
        EstimateIsWarning = hasErrors || (missing.Count == 0 && age < BodyLimits.AdultAge);
        EstimateMessage =
            hasErrors ? "Fix the highlighted fields to see your estimate."
            : missing.Count > 0 ? $"Add your {JoinList(missing)} to see how many calories you burn in a day."
            : GenderIndex == PreferNotToSay ? "The formula uses male or female, so there is no estimate. You can still set a calorie goal by hand."
            : estimate.Reason ?? "";

        var anyFilled = GenderIndex >= 0 || !BirthDateEmpty || !string.IsNullOrWhiteSpace(HeightCmText)
                        || !string.IsNullOrWhiteSpace(HeightFtText) || !string.IsNullOrWhiteSpace(HeightInText)
                        || !string.IsNullOrWhiteSpace(WeightText) || SelectedActivity is not null || ChosenCity is not null;
        CanSave = anyFilled && !hasErrors;
        SaveHintIsError = hasErrors;
        SaveHint = hasErrors ? "Fix the highlighted fields first."
            : !anyFilled ? "Nothing to save yet. Fill in anything above, or skip."
            : null;
    }

    // Runs on the Search button or Enter, never per keystroke: Nominatim allows one request per second
    [RelayCommand]
    private async Task SearchCityAsync()
    {
        try
        {
            CitySearchMessage = null;
            CanRetryCitySearch = false;
            CityCandidates.Clear();
            HasCityCandidates = false;

            var query = CitySearchText.Trim();
            var r = await _geocoding.SearchAsync(query);
            if (!r.Success) { CitySearchMessage = r.Error; CanRetryCitySearch = r.CanRetry; return; }
            if (r.Candidates.Count == 0) { CitySearchMessage = $"No cities found for \"{query}\"."; return; }

            foreach (var c in r.Candidates) CityCandidates.Add(new CityCandidateViewModel(c, ChooseCity));
            HasCityCandidates = true;
        }
        catch (Exception)
        {
            CitySearchMessage = "Could not search for cities.";
        }
    }

    private Task ChooseCity(CityCandidate candidate)
    {
        ChosenCity = candidate;
        CityCandidates.Clear();
        HasCityCandidates = false;
        CitySearchText = "";
        CitySearchMessage = null;
        return Task.CompletedTask;
    }

    [RelayCommand] private void ChangeCity() => ChosenCity = null;

    private bool CanSaveNow() => CanSave && !IsBusy;

    /// <summary>Saves what was filled in: the profile, today's weigh-in and the home city, then shows the summary.</summary>
    [RelayCommand(CanExecute = nameof(CanSaveNow))]
    private async Task SaveAsync()
    {
        Recompute();
        if (!CanSave) return;
        IsBusy = true;
        try
        {
            Error = null;
            var birth = ReadBirthDate().Date;
            var height = ReadHeight(SelectedUnits).Cm;
            var weight = ReadWeight().Kg;

            var saved = await _profiles.SaveAsync(UserId, new ProfileInput(GenderAsSex, birth, height, SelectedActivity?.Level, SelectedUnits));
            if (!saved.Success) { Error = saved.Error; return; }
            if (weight is { } kg)
            {
                var w = await _profiles.LogWeightAsync(UserId, kg);
                if (!w.Success) { Error = w.Error; return; }
            }
            if (ChosenCity is { } candidate)
            {
                var city = await _cities.UpsertAsync(candidate);
                var c = await _profiles.SetHomeCityAsync(UserId, city.Id);
                if (!c.Success) { Error = c.Error; return; }
            }

            var summary = await _profiles.GetAsync(UserId);
            ShowSummary(birth, height, weight, summary?.Maintenance);
            IsDone = true;
        }
        catch (Exception)
        {
            Error = "Could not save your profile. Try again, or skip for now.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ShowSummary(DateOnly? birth, double? cm, double? kg, MaintenanceEstimate? maintenance)
    {
        string? gender = GenderIndex switch { Male => "Male", Female => "Female", PreferNotToSay => "Prefer not to say", _ => null };
        (string Label, string? Value)[] rows =
        [
            ("Gender", gender),
            ("Age", birth is { } b ? BodyMetrics.AgeOn(b, Today).ToString(CultureInfo.InvariantCulture) : null),
            ("Height", cm is { } h ? Units.FormatHeight(h, SelectedUnits) : null),
            ("Weight", kg is { } w ? $"{Units.FormatWeight(w, SelectedUnits)} · today" : null),
            ("Activity", SelectedActivity?.Name),
            ("Home city", ChosenCity?.Name)
        ];
        Summary = rows.Select((r, i) => new SetupSummaryRow(r.Label, r.Value ?? "Not set", r.Value is not null, i == 0)).ToList();

        HasSavedEstimate = maintenance?.Kcal is not null;
        SavedEstimateText = maintenance?.Kcal is { } kcal ? $"≈ {kcal.ToString("N0", CultureInfo.InvariantCulture)}" : "";
        SavedEstimateMessage = GenderIndex == PreferNotToSay && maintenance?.Kcal is null
            ? "No estimate yet. The formula uses male or female. You can set a goal by hand in Calories."
            : $"No estimate yet. {maintenance?.Reason ?? ""}".TrimEnd();
        IsGoalSet = false;
    }

    private bool CanUseAsGoal() => !IsBusy;

    /// <summary>Makes the maintenance estimate the daily goal (the Calories screen's suggestion, with the default
    /// Maintain goal type).</summary>
    [RelayCommand(CanExecute = nameof(CanUseAsGoal))]
    private async Task UseAsGoalAsync()
    {
        IsBusy = true;
        try
        {
            var r = await _calories.AcceptSuggestionAsync(UserId);
            if (!r.Success) { Error = r.Error; return; }
            Error = null;
            IsGoalSet = true;
        }
        catch (Exception)
        {
            Error = "Could not set your calorie goal.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Back to the form; saving again overwrites what was saved.</summary>
    [RelayCommand] private void EditAnswers() => IsDone = false;

    [RelayCommand] private void Skip() => _shell().ShowHome();
    [RelayCommand] private void Finish() => _shell().ShowHome();
}
