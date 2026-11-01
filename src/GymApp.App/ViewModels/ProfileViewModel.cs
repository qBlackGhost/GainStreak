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

/// <summary>One weigh-in on the Profile's weight chart: its value in the shown unit, and the bubble's two lines.</summary>
public record WeightChartPoint(double Value, string ValueText, string DateText);

/// <summary>One option of the activity selector: "Less active", "under 7,500 steps".</summary>
public partial class ActivityOption(ActivityLevel level, Action<ActivityOption>? pick = null) : ObservableObject
{
    public ActivityLevel Level { get; } = level;
    public string Name => ActivityLevels.DisplayName(Level);
    public string Range => ActivityLevels.StepRange(Level);
    public string Steps => Range.Replace(" / day", "");
    /// <summary>The phones' compact choices: "Less active" over "&lt;7.5k".</summary>
    public string ShortName => Name[..1] + Name[1..].ToLowerInvariant();
    public string ShortSteps => Level switch
    {
        ActivityLevel.LessActive => "<7.5k",
        ActivityLevel.Active => "7.5–12.5k",
        ActivityLevel.VeryActive => ">12.5k",
        _ => ""
    };

    [ObservableProperty] public partial bool IsSelected { get; set; }

    [RelayCommand] private void Pick() => pick?.Invoke(this);
}

/// <summary>A month in the birth date picker.</summary>
public record MonthOption(int Number, string Name);

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
    private readonly IAccountService _accounts;
    private readonly GeocodingService _geocoding;
    private readonly CityService _cities;
    private readonly SessionService _session;
    private readonly Func<MainViewModel> _shell;

    public ProfileViewModel(ProfileService profiles, IAccountService accounts, GeocodingService geocoding,
        CityService cities, SessionService session, Func<MainViewModel> shell,
        ReminderService reminders, INotificationScheduler notifications, ReminderSync reminderSync)
    {
        _profiles = profiles; _accounts = accounts; _geocoding = geocoding; _cities = cities;
        _session = session; _shell = shell;
        Reminders = new RemindersViewModel(reminders, notifications, reminderSync, () => session.CurrentUser?.Id);
        _ = Reminders.LoadAsync();
        ActivityOptions = Enum.GetValues<ActivityLevel>().Select(l => new ActivityOption(l, o => SelectedActivity = o)).ToList();
        ThemeIndex = (int)(session.CurrentUser?.ThemePreference ?? ThemePreference.System);
        _ = ReloadAsync();
    }

    private int UserId => _session.CurrentUser!.Id;

    /// <summary>Reminders (16g): the phones only.</summary>
    public RemindersViewModel Reminders { get; }
    private static DateOnly Today => AppClock.Today;

    public IReadOnlyList<Sex> Sexes { get; } = Enum.GetValues<Sex>();
    public IReadOnlyList<UnitSystem> UnitSystems { get; } = Enum.GetValues<UnitSystem>();
    public IReadOnlyList<ActivityOption> ActivityOptions { get; }
    public IReadOnlyList<MonthOption> Months { get; } = Enumerable.Range(1, 12)
        .Select(m => new MonthOption(m, CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(m))).ToList();
    public ObservableCollection<WeightEntryViewModel> History { get; } = new();
    public ObservableCollection<CityCandidateViewModel> CityCandidates { get; } = new();

    // ===== Header =====
    [ObservableProperty] public partial string Username { get; set; } = "";
    [ObservableProperty] public partial string Initials { get; set; } = "";
    [ObservableProperty] public partial string? Avatar { get; set; }
    [ObservableProperty] public partial int AvatarLevel { get; set; }
    [ObservableProperty] public partial string SplitText { get; set; } = "";
    [ObservableProperty] public partial bool HasSplit { get; set; }
    [ObservableProperty] public partial string StreakText { get; set; } = "";
    [ObservableProperty] public partial string CurrentStreak { get; set; } = "0";
    [ObservableProperty] public partial string BestStreak { get; set; } = "0";
    [ObservableProperty] public partial string LevelText { get; set; } = "";
    // The phones' header tiles
    [ObservableProperty] public partial string LevelNumber { get; set; } = "";
    [ObservableProperty] public partial string LevelName { get; set; } = "";
    [ObservableProperty] public partial string Points { get; set; } = "0";

    // ===== Account =====
    [ObservableProperty] public partial string EmailText { get; set; } = "";
    [ObservableProperty] public partial bool HasEmail { get; set; }
    /// <summary>The reminder under the email. Not when offline, when the app doesn't know.</summary>
    [ObservableProperty] public partial bool EmailNeedsConfirming { get; set; }
    [ObservableProperty] public partial bool IsEditingEmail { get; set; }
    [ObservableProperty] public partial string NewEmail { get; set; } = "";
    [ObservableProperty] public partial string EmailPassword { get; set; } = "";
    [ObservableProperty] public partial string? EmailError { get; set; }
    [ObservableProperty] public partial string? EmailPasswordError { get; set; }
    public string EditEmailLabel => HasEmail ? "Change" : "Add email";
    partial void OnHasEmailChanged(bool value) => OnPropertyChanged(nameof(EditEmailLabel));

    [ObservableProperty] public partial bool IsChangingPassword { get; set; }
    [ObservableProperty] public partial string CurrentPassword { get; set; } = "";
    [ObservableProperty] public partial string NewPassword { get; set; } = "";
    [ObservableProperty] public partial string ConfirmNewPassword { get; set; } = "";
    [ObservableProperty] public partial string? CurrentPasswordError { get; set; }
    [ObservableProperty] public partial string? NewPasswordError { get; set; }
    [ObservableProperty] public partial string? ConfirmNewPasswordError { get; set; }

    [ObservableProperty] public partial bool IsConfirmingDelete { get; set; }
    [ObservableProperty] public partial string DeletePassword { get; set; } = "";
    [ObservableProperty] public partial string? DeleteError { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveEmailCommand), nameof(SavePasswordCommand), nameof(DeleteAccountCommand))]
    public partial bool IsAccountBusy { get; set; }
    private bool CanChangeAccount() => !IsAccountBusy;

    // ===== Body data form =====
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SexIndex))]
    public partial Sex? SelectedSex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AgeText), nameof(AgeNumber))]
    public partial string BirthDayText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AgeText), nameof(AgeNumber))]
    public partial MonthOption? BirthMonth { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AgeText), nameof(AgeNumber))]
    public partial string BirthYearText { get; set; } = "";

    [ObservableProperty] public partial string HeightCmText { get; set; } = "";
    [ObservableProperty] public partial string HeightFtText { get; set; } = "";
    [ObservableProperty] public partial string HeightInText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActivityIndex))]
    public partial ActivityOption? SelectedActivity { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMetric), nameof(IsImperial), nameof(WeightWatermark), nameof(UnitsIndex))]
    public partial UnitSystem SelectedUnits { get; set; } = UnitSystem.Metric;

    public bool IsMetric => SelectedUnits == UnitSystem.Metric;
    public bool IsImperial => SelectedUnits == UnitSystem.Imperial;
    public string WeightWatermark => IsMetric ? "e.g. 66.2" : "e.g. 146";

    // The segmented choices (sliding pills); -1 = nothing chosen yet
    public int UnitsIndex => IsMetric ? 0 : 1;
    public int SexIndex => SelectedSex switch { Sex.Male => 0, Sex.Female => 1, _ => -1 };
    public int ActivityIndex => SelectedActivity is { } a ? ActivityOptions.ToList().FindIndex(o => o.Level == a.Level) : -1;

    [RelayCommand] private void SelectMetric() => SelectedUnits = UnitSystem.Metric;
    [RelayCommand] private void SelectImperial() => SelectedUnits = UnitSystem.Imperial;
    [RelayCommand] private void SelectMale() => SelectedSex = Sex.Male;
    [RelayCommand] private void SelectFemale() => SelectedSex = Sex.Female;

    /// <summary>"Age 21" from the three birth date boxes, or "Age —" until they make a real date.</summary>
    public string AgeText => $"Age {AgeNumber}";
    /// <summary>The age alone, for the phones' Age box ("31", or "—" without a birth date).</summary>
    public string AgeNumber => TryReadBirthDate(out var b) && b is { } d ? BodyMetrics.AgeOn(d, Today).ToString(CultureInfo.InvariantCulture) : "—";

    /// <summary>Something in the body data form changed since it was filled or saved.</summary>
    [ObservableProperty] public partial bool IsDirty { get; set; }

    [ObservableProperty] public partial string MaintenanceText { get; set; } = "";
    [ObservableProperty] public partial string? MaintenanceNote { get; set; }

    // ===== Weight =====
    [ObservableProperty] public partial string WeightInput { get; set; } = "";
    [ObservableProperty] public partial string CurrentWeightText { get; set; } = "";
    [ObservableProperty] public partial string CurrentWeightNumber { get; set; } = "—";
    [ObservableProperty] public partial string WeightUnit { get; set; } = "kg";
    [ObservableProperty] public partial string CurrentWeightDate { get; set; } = "No weigh-ins yet";
    [ObservableProperty] public partial string DeltaText { get; set; } = "First weigh-in";
    [ObservableProperty] public partial string LogWeightLabel { get; set; } = "Log today's weight";
    [ObservableProperty] public partial bool HasHistory { get; set; }
    [ObservableProperty] public partial bool HasTrend { get; set; }
    /// <summary>The trend chart's weigh-ins, oldest to newest, in the shown unit.</summary>
    [ObservableProperty] public partial IReadOnlyList<WeightChartPoint> TrendPoints { get; set; } = [];
    [ObservableProperty] public partial string TrendFirstLabel { get; set; } = "";
    [ObservableProperty] public partial string TrendLastLabel { get; set; } = "";

    private const int TrendCount = 8, RecentShown = 5;

    // ===== Appearance: System, Light, Dark (12e); applies at once and is saved on the user =====
    [ObservableProperty] public partial int ThemeIndex { get; set; }
    [ObservableProperty] public partial string? ThemeError { get; set; }

    [RelayCommand]
    private async Task SelectTheme(string index)
    {
        var theme = (ThemePreference)int.Parse(index, CultureInfo.InvariantCulture);
        if (_session.CurrentUser is not { } user || user.ThemePreference == theme) return;
        ThemeIndex = (int)theme;
        user.ThemePreference = theme;
        AppTheme.Apply(theme);
        var saved = await _profiles.SetThemeAsync(user.Id, theme);
        ThemeError = saved.Success ? null : saved.Error;
    }

    // ===== Home city =====
    [ObservableProperty] public partial string HomeCityText { get; set; } = "";
    [ObservableProperty] public partial string HomeCityName { get; set; } = "";
    [ObservableProperty] public partial string HomeCityRegion { get; set; } = "";
    [ObservableProperty] public partial bool HasHomeCity { get; set; }
    [ObservableProperty] public partial string CitySearchText { get; set; } = "";
    [ObservableProperty] public partial string? CitySearchMessage { get; set; }
    [ObservableProperty] public partial bool CanRetryCitySearch { get; set; }
    [ObservableProperty] public partial bool HasCityCandidates { get; set; }

    /// <summary>"GainStreak 0.14.1", from the assembly version (Directory.Build.props).</summary>
    public string VersionText { get; } = $"GainStreak {AppVersion.Current}";

    // The phones have no sidebar, so Profile holds these there
    [RelayCommand]
    private void EditAvatar() => _shell().OpenAvatarPicker(Avatar, Initials, AvatarLevel, async () =>
    {
        await _shell().RefreshAvatarAsync();
        Avatar = _shell().OwnAvatar;
    });

    [RelayCommand] private void SendFeedback() => _shell().ShowFeedback();
    [RelayCommand] private Task LogOut() => _accounts.LogOutAsync();

    [ObservableProperty] public partial string? Error { get; set; }

    // Messages shown under the field they're about
    [ObservableProperty] public partial string? UnitsError { get; set; }
    [ObservableProperty] public partial string? SexError { get; set; }
    [ObservableProperty] public partial string? BirthDateError { get; set; }
    [ObservableProperty] public partial string? HeightError { get; set; }
    [ObservableProperty] public partial string? ActivityError { get; set; }
    [ObservableProperty] public partial string? WeightError { get; set; }

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
            ApplyEmail(s.Email);
            Initials = s.Username.Length == 0 ? "?" : s.Username[..Math.Min(2, s.Username.Length)].ToUpperInvariant();
            Avatar = s.Profile.Avatar;
            HasSplit = s.SplitName is not null;
            SplitText = s.SplitName?.Replace(" (", " · ").TrimEnd(')') ?? "No split selected";
            StreakText = $"{s.CurrentStreak} day streak · best {s.LongestStreak}";
            CurrentStreak = s.CurrentStreak.ToString(CultureInfo.InvariantCulture);
            BestStreak = s.LongestStreak.ToString(CultureInfo.InvariantCulture);
            var level = LevelCalculator.For(_session.CurrentUser?.Points ?? 0);
            AvatarLevel = level.Number;
            LevelText = $"Level {level.Number} · {level.Name}";
            LevelNumber = level.Number.ToString(CultureInfo.InvariantCulture);
            LevelName = level.Name;
            Points = (_session.CurrentUser?.Points ?? 0).ToString("N0", CultureInfo.InvariantCulture);
            ApplyMaintenance(s.Maintenance);

            if (refillForm) FillForm(s.Profile);
            ApplyHomeCity(s.Profile.HomeCity);

            // Weight and history are shown in the saved unit, so they match what the form says after a reload
            var units = s.Profile.Units;
            CurrentWeightText = s.CurrentWeight is { } w
                ? $"{Units.FormatWeight(w.WeightKg, units)} ({DayLabel(w.Date)})"
                : "No weight logged yet";
            History.Clear();
            foreach (var entry in s.RecentWeights.Take(RecentShown)) History.Add(new WeightEntryViewModel(entry, units, this));
            HasHistory = History.Count > 0;
            ApplyWeight(s.RecentWeights, units);

        }
        catch (Exception)
        {
            Error = "Could not load your profile.";
        }
    }

    private void FillForm(UserProfile p)
    {
        _loading = true;   // so switching units while filling does not convert the boxes or mark the form dirty
        SelectedUnits = p.Units;
        SelectedSex = p.Sex;
        BirthDayText = p.BirthDate?.Day.ToString(CultureInfo.InvariantCulture) ?? "";
        BirthMonth = p.BirthDate is { } b ? Months[b.Month - 1] : null;
        BirthYearText = p.BirthDate?.Year.ToString(CultureInfo.InvariantCulture) ?? "";
        SetHeightFields(p.HeightCm, p.Units);
        SelectedActivity = p.ActivityLevel is { } a ? ActivityOptions.First(o => o.Level == a) : null;
        _loading = false;
        IsDirty = false;
    }

    // Any edit to the body data form enables Save
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_loading) return;
        if (e.PropertyName is nameof(SelectedUnits) or nameof(SelectedSex) or nameof(BirthDayText) or nameof(BirthMonth)
            or nameof(BirthYearText) or nameof(HeightCmText) or nameof(HeightFtText) or nameof(HeightInText)
            or nameof(SelectedActivity))
            IsDirty = true;
    }

    partial void OnSelectedActivityChanged(ActivityOption? value)
    {
        foreach (var o in ActivityOptions) o.IsSelected = o == value;
    }

    /// <summary>Empty boxes are fine (no birth date); otherwise day, month and year must make a real date.</summary>
    private bool TryReadBirthDate(out DateOnly? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(BirthDayText) && BirthMonth is null && string.IsNullOrWhiteSpace(BirthYearText)) return true;
        if (!NumberParsing.TryParseWhole(BirthDayText, out var d) || BirthMonth is not { } m
            || !NumberParsing.TryParseWhole(BirthYearText, out var y)
            || y < 1 || y > 9999 || d < 1 || d > DateTime.DaysInMonth(y, m.Number))
            return false;
        date = new DateOnly(y, m.Number, d);
        return true;
    }

    private void ApplyMaintenance(MaintenanceEstimate m)
    {
        MaintenanceText = m.Kcal is { } kcal ? $"≈ {kcal.ToString("N0", CultureInfo.InvariantCulture)}" : "—";
        MaintenanceNote = m.Kcal is null ? m.Reason : null;
    }

    /// <summary>The weight card: the latest weigh-in, the change since the one before, and the trend line.</summary>
    private void ApplyWeight(IReadOnlyList<WeightLog> recent, UnitSystem units)
    {
        WeightUnit = units == UnitSystem.Metric ? "kg" : "lb";
        double Shown(double kg) => units == UnitSystem.Metric ? kg : Units.KgToLb(kg);
        var cur = recent.FirstOrDefault();
        CurrentWeightNumber = cur is null ? "—" : Shown(cur.WeightKg).ToString("0.#", CultureInfo.InvariantCulture);
        CurrentWeightDate = cur is null ? "No weigh-ins yet"
            : $"Last weigh-in · {cur.Date.ToString("ddd d MMM yyyy", CultureInfo.InvariantCulture)}";
        LogWeightLabel = cur?.Date == Today ? "Replace today's" : "Log today's weight";

        if (recent.Count > 1)
        {
            var prev = recent[1];
            var delta = Math.Round(Shown(cur!.WeightKg) - Shown(prev.WeightKg), 1);
            var sign = delta > 0 ? "+" : delta < 0 ? "−" : "±";
            DeltaText = $"{sign}{Math.Abs(delta).ToString("0.#", CultureInfo.InvariantCulture)} {WeightUnit} since " +
                        prev.Date.ToString("d MMM", CultureInfo.InvariantCulture);
        }
        else
        {
            DeltaText = "First weigh-in";
        }

        // Oldest to newest; the chart scales them and shows a tapped one's weight and date
        var pts = recent.Take(TrendCount).Reverse().ToList();
        HasTrend = pts.Count > 1;
        if (!HasTrend) { TrendPoints = []; return; }
        TrendPoints = pts.Select(p =>
        {
            var v = Shown(p.WeightKg);
            return new WeightChartPoint(v, $"{v.ToString("0.#", CultureInfo.InvariantCulture)} {WeightUnit}",
                p.Date.ToString("ddd d MMM", CultureInfo.InvariantCulture));
        }).ToList();
        TrendFirstLabel = pts[0].Date.ToString("d MMM", CultureInfo.InvariantCulture);
        TrendLastLabel = pts[^1].Date.ToString("d MMM", CultureInfo.InvariantCulture);
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

            if (!TryReadBirthDate(out var birth)) { BirthDateError = "Enter a real date: day, month and year."; return; }

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
            await ReloadAsync();
            Info = MaintenanceText == "—" ? "Profile saved." : $"Profile saved. Maintenance is now about {MaintenanceText.TrimStart('≈', ' ')} kcal a day.";

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

    // ===== Account email =====

    private void ApplyEmail(string? email)
    {
        HasEmail = email is not null;
        EmailNeedsConfirming = HasEmail && _accounts.EmailConfirmed == false;
        EmailText = email ?? "No email yet. Add one so you can sign in with it.";
    }

    [RelayCommand]
    private void ConfirmEmail() => _shell().ShowConfirmEmail();

    [RelayCommand]
    private void EditEmail()
    {
        NewEmail = HasEmail ? EmailText : "";
        EmailPassword = "";
        EmailError = EmailPasswordError = null;
        IsEditingEmail = true;
    }

    [RelayCommand]
    private void CancelEmail()
    {
        IsEditingEmail = false;
        NewEmail = EmailPassword = "";
        EmailError = EmailPasswordError = null;
    }

    [RelayCommand(CanExecute = nameof(CanChangeAccount))]
    private async Task SaveEmailAsync()
    {
        IsAccountBusy = true;
        try
        {
            Info = null;
            Error = null;
            EmailError = EmailPasswordError = null;
            var r = await _accounts.ChangeEmailAsync(NewEmail, EmailPassword);
            if (!r.Success)
            {
                switch (r.Field)
                {
                    case FormField.Password: EmailPasswordError = r.Error; break;
                    default: EmailError = r.Error; break;
                }
                return;
            }

            ApplyEmail(r.User!.Email);
            CancelEmail();
            Info = $"Email saved. You can now sign in with {r.User.Email}.";
        }
        catch (Exception)
        {
            EmailError = "Could not save your email.";
        }
        finally
        {
            IsAccountBusy = false;
        }
    }

    // ===== Account password =====

    [RelayCommand]
    private void ChangePassword()
    {
        CancelDelete();
        CancelPassword();
        IsChangingPassword = true;
    }

    [RelayCommand]
    private void CancelPassword()
    {
        IsChangingPassword = false;
        CurrentPassword = NewPassword = ConfirmNewPassword = "";
        CurrentPasswordError = NewPasswordError = ConfirmNewPasswordError = null;
    }

    [RelayCommand(CanExecute = nameof(CanChangeAccount))]
    private async Task SavePasswordAsync()
    {
        Info = Error = null;
        CurrentPasswordError = NewPasswordError = ConfirmNewPasswordError = null;
        if (NewPassword != ConfirmNewPassword) { ConfirmNewPasswordError = "Passwords do not match."; return; }

        IsAccountBusy = true;
        try
        {
            var r = await _accounts.ChangePasswordAsync(CurrentPassword, NewPassword);
            if (!r.Success)
            {
                switch (r.Field)
                {
                    case FormField.Password: CurrentPasswordError = r.Error; break;
                    case FormField.NewPassword: NewPasswordError = r.Error; break;
                    default: CurrentPasswordError = r.Error; break;
                }
                return;
            }
            CancelPassword();
            Info = "Password changed. Other devices have to sign in again.";
        }
        catch (Exception)
        {
            CurrentPasswordError = "Could not change your password.";
        }
        finally
        {
            IsAccountBusy = false;
        }
    }

    // ===== Deleting the account =====

    /// <summary>What the confirmation says is deleted, so nobody is surprised.</summary>
    public string DeleteWarning =>
        "This deletes your online account (friends, leaderboard and shared check-ins) and every workout, set, PR, " +
        "weigh-in, calorie log and point on this computer. It can't be undone.";

    [RelayCommand]
    private void AskDelete()
    {
        CancelPassword();
        DeletePassword = "";
        DeleteError = null;
        IsConfirmingDelete = true;
    }

    [RelayCommand]
    private void CancelDelete()
    {
        IsConfirmingDelete = false;
        DeletePassword = "";
        DeleteError = null;
    }

    [RelayCommand(CanExecute = nameof(CanChangeAccount))]
    private async Task DeleteAccountAsync()
    {
        DeleteError = null;
        IsAccountBusy = true;
        try
        {
            var r = await _accounts.DeleteAccountAsync(DeletePassword);
            if (!r.Success) DeleteError = r.Error;   // on success the app signs out to the login screen
        }
        catch (Exception)
        {
            DeleteError = "Could not delete your account. Try again.";
        }
        finally
        {
            IsAccountBusy = false;
        }
    }

    // ===== Home city =====

    private void ApplyHomeCity(City? city)
    {
        HasHomeCity = city is not null;
        HomeCityText = city?.DisplayName ?? "Not set. The gym picker will ask for a city.";
        // "Timișoara, Timiș, România" → "Timișoara" over "Timiș, România"
        var parts = (city?.DisplayName ?? "").Split(", ", 2);
        HomeCityName = parts[0];
        HomeCityRegion = parts.Length > 1 ? parts[1] : "";
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

    [RelayCommand]
    private void Back() => _shell().ShowHome();

    [RelayCommand]
    private void ShowCalories() => _shell().ShowCalories();

    [RelayCommand]
    private void ShowSplits() => _shell().ShowSplits();

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
