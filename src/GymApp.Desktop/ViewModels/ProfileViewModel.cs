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
    private readonly SessionService _session;
    private readonly Func<MainViewModel> _shell;

    public ProfileViewModel(ProfileService profiles, SessionService session, Func<MainViewModel> shell)
    {
        _profiles = profiles; _session = session; _shell = shell;
        _ = ReloadAsync();
    }

    private int UserId => _session.CurrentUser!.Id;
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public IReadOnlyList<Sex> Sexes { get; } = Enum.GetValues<Sex>();
    public IReadOnlyList<UnitSystem> UnitSystems { get; } = Enum.GetValues<UnitSystem>();
    public ObservableCollection<WeightEntryViewModel> History { get; } = new();

    // The birth date picker only offers years that pass validation
    public DateTimeOffset MinBirthYear { get; } = new(DateTime.Today.Year - BodyLimits.MaxAge, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public DateTimeOffset MaxBirthYear { get; } = new(DateTime.Today.Year - BodyLimits.MinAge, 12, 31, 0, 0, 0, TimeSpan.Zero);

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
    [ObservableProperty] public partial string StepsText { get; set; } = "";

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

    [ObservableProperty] public partial string? Error { get; set; }
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

            // Weight and history are shown in the saved unit, so they match what the form says after a reload
            var units = s.Profile.Units;
            CurrentWeightText = s.CurrentWeight is { } w
                ? $"{Units.FormatWeight(w.WeightKg, units)} ({DayLabel(w.Date)})"
                : "No weight logged yet";
            History.Clear();
            foreach (var entry in s.RecentWeights) History.Add(new WeightEntryViewModel(entry, units, this));
            HasHistory = History.Count > 0;

            ApplyMaintenance(s.Maintenance);
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
        StepsText = p.StepsPerDay?.ToString(CultureInfo.InvariantCulture) ?? "";
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
                              "(Mifflin-St Jeor, from your daily steps).";
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
        if (!int.TryParse(HeightFtText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var ft))
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
        Info = null;
        if (!TryReadHeight(SelectedUnits, out var cm, out var heightError)) { Error = heightError; return; }

        int? steps = null;
        if (!string.IsNullOrWhiteSpace(StepsText))
        {
            if (!Units.TryParseSteps(StepsText, out var s)) { Error = "Daily steps must be a whole number."; return; }
            steps = s;
        }

        DateOnly? birth = BirthDate is { } b ? DateOnly.FromDateTime(b.Date) : null;

        var r = await _profiles.SaveAsync(UserId, new ProfileInput(SelectedSex, birth, cm, steps, SelectedUnits));
        if (!r.Success) { Error = r.Error; return; }

        Error = null;
        Info = "Profile saved.";
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task LogWeightAsync()
    {
        Info = null;
        if (!Units.TryParseNumber(WeightInput, out var value))
        {
            Error = IsMetric ? "Enter your weight in kg, e.g. 80.5." : "Enter your weight in lb, e.g. 177.";
            return;
        }

        var kg = IsMetric ? value : Units.LbToKg(value);
        var r = await _profiles.LogWeightAsync(UserId, kg);
        if (!r.Success) { Error = r.Error; return; }

        Error = null;
        WeightInput = "";
        Info = "Weight saved for today.";
        await ReloadAsync(refillForm: false);   // keep any unsaved edits in the form
    }

    internal async Task DeleteWeightAsync(WeightEntryViewModel item)
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

    [RelayCommand]
    private void Back() => _shell().ShowHome();

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
