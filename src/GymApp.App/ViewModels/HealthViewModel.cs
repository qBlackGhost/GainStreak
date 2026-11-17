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

/// <summary>
/// Profile → Apple Health (Phase 20) on the iPhone, Health Connect (Phase 23) on Android
/// (<see cref="IHealthStore.IsSupported"/>; the words come from <see cref="IHealthStore.Texts"/>). Off until the user
/// turns it on, which shows iOS's permission sheet, saves the setting and starts a sync (the first one writes the last
/// 30 days). The kinds the user didn't allow in the sheet are named with a way to Settings; reads can't be checked (iOS
/// hides them), so nothing is said about them. Turning it off stops syncing; what's in Health stays there.
/// <para>20c: while it's on, opening Profile reads from Health: new weigh-ins are imported, empty profile fields
/// filled (then <c>profileChanged</c> reloads Profile), and the card offers Health's values where they differ from
/// typed ones, and an activity level from steps. Each offer is answered once.</para>
/// </summary>
public partial class HealthViewModel : ObservableObject
{
    /// <summary>The platform's words (Phase 23): Apple Health on the iPhone, Health Connect on Android.</summary>
    public HealthTexts Texts => _store.Texts;

    public string TurnedOffText => $"What GainStreak already saved stays in {Texts.Store}. You can delete it in {Texts.App}.";
    public string NoSheetText => $"{Texts.Title} couldn't be opened. Try again.";
    public string InstallText => $"{Texts.Title} isn't on this phone yet. Install it from Google Play, then turn this on again.";
    public string UseStoreText => $"Use {Texts.Store}'s";

    private readonly IHealthStore _store;
    private readonly ProfileService _profiles;
    private readonly HealthSync _sync;
    private readonly HealthImportService? _import;
    private readonly Func<int?> _userId;
    private readonly Func<UnitSystem> _units;
    private readonly Action _profileChanged;
    private bool _loading;
    private HealthProfileOffer? _offer;
    private ActivityLevel? _suggested;

    public HealthViewModel(IHealthStore store, ProfileService profiles, HealthSync sync, Func<int?> userId,
        HealthImportService? import = null, Func<UnitSystem>? units = null, Action? profileChanged = null)
    {
        _store = store;
        _profiles = profiles;
        _sync = sync;
        _import = import;
        _userId = userId;
        _units = units ?? (() => UnitSystem.Metric);
        _profileChanged = profileChanged ?? (() => { });
    }

    /// <summary>False on the desktop, and where the phone has no Health store: the card is hidden.</summary>
    public bool IsSupported => _store.IsSupported;

    [ObservableProperty] public partial bool IsOn { get; set; }

    /// <summary>Kinds the user didn't allow ("Weigh-ins are off in Health's settings…"), or why the switch went back
    /// off; shown with Open Settings when <see cref="CanOpenSettings"/>.</summary>
    [ObservableProperty] public partial string? Problem { get; set; }
    [ObservableProperty] public partial bool CanOpenSettings { get; set; }

    /// <summary>A quiet line: after turning it off, or the weigh-ins just imported.</summary>
    [ObservableProperty] public partial string? Note { get; set; }

    /// <summary>"Health says: height 182 cm, born 3 Feb 1994." with Use Health's / Keep mine (20c).</summary>
    [ObservableProperty] public partial string? OfferText { get; set; }

    /// <summary>"Your steps say Active: 9,800 a day over the last 30 days." with Use it / Not now (20c).</summary>
    [ObservableProperty] public partial string? StepsText { get; set; }

    public async Task LoadAsync()
    {
        if (!IsSupported || _userId() is not { } id) return;
        _loading = true;
        IsOn = await _profiles.IsHealthEnabledAsync(id);
        _loading = false;
        CheckAccess();
        if (IsOn) await ReadAsync();
    }

    /// <summary>Reads from Health now and shows what came of it.</summary>
    private async Task ReadAsync()
    {
        if (_import is null) return;
        await _store.RequestAuthorizationAsync();   // only types never asked before (20c added reads) show the sheet
        CheckAccess();   // Android knows what was granted only after asking
        if (await _sync.ReadAsync(force: true) is not { } r) return;
        _offer = r.Offer;
        OfferText = r.Offer is { } o ? DescribeOffer(o) : null;
        _suggested = r.StepsSuggestion;
        StepsText = r.StepsSuggestion is { } level && r.AverageSteps is { } avg
            ? $"Your steps say {ActivityLevels.DisplayName(level)}: {avg.ToString("N0", CultureInfo.InvariantCulture)} a day over the last {HealthImport.StepDays} days."
            : null;
        if (r.WeighInsImported > 0)
            Note = r.WeighInsImported == 1 ? $"1 weigh-in from {Texts.Store} added." : $"{r.WeighInsImported} weigh-ins from {Texts.Store} added.";
        if (r.ProfileFilled || r.WeighInsImported > 0) _profileChanged();
    }

    private string DescribeOffer(HealthProfileOffer o)
    {
        var parts = new List<string>();
        if (o.HeightCm is { } h) parts.Add($"height {Units.FormatHeight(h, _units())}");
        if (o.BirthDate is { } b) parts.Add($"born {b.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}");
        if (o.Sex is { } s) parts.Add(s == Sex.Female ? "female" : "male");
        return $"{Texts.Store} says: {string.Join(", ", parts)}.";
    }

    [RelayCommand] private Task UseHealthProfile() => AnswerOfferAsync(true);
    [RelayCommand] private Task KeepMyProfile() => AnswerOfferAsync(false);

    private async Task AnswerOfferAsync(bool useHealth)
    {
        if (_import is null || _offer is not { } offer || _userId() is not { } id) return;
        await _import.AnswerProfileOfferAsync(id, offer, useHealth);
        _offer = null;
        OfferText = null;
        if (useHealth) _profileChanged();
    }

    [RelayCommand] private Task UseStepsLevel() => AnswerStepsAsync(true);
    [RelayCommand] private Task NotNowSteps() => AnswerStepsAsync(false);

    private async Task AnswerStepsAsync(bool use)
    {
        if (_import is null || _suggested is not { } level || _userId() is not { } id) return;
        await _import.AnswerStepsSuggestionAsync(id, level, use);
        _suggested = null;
        StepsText = null;
        if (use) _profileChanged();
    }

    /// <summary>A weigh-in was logged or deleted: to Health now, not when the app goes to the back.</summary>
    public void Changed() => _sync.RequestSync();

    partial void OnIsOnChanged(bool value)
    {
        if (_loading) return;
        _ = value ? TurnOnAsync() : TurnOffAsync();
    }

    private async Task TurnOnAsync()
    {
        Note = null;
        if (_userId() is not { } id) return;
        if (_store.NeedsInstall)
        {
            // Health Connect on Android 8 to 13: install it first; the switch stays off until then
            SetQuietly(false);
            Problem = InstallText;
            CanOpenSettings = false;
            _store.OpenInstallPage();
            return;
        }
        if (!await _store.RequestAuthorizationAsync())
        {
            SetQuietly(false);
            Problem = NoSheetText;
            CanOpenSettings = false;
            return;
        }
        if (!(await _profiles.SetHealthEnabledAsync(id, true)).Success)
        {
            SetQuietly(false);
            Problem = "Could not save that setting.";
            return;
        }
        CheckAccess();
        _sync.RequestSync();
        await ReadAsync();
    }

    private async Task TurnOffAsync()
    {
        Problem = null;
        CanOpenSettings = false;
        if (_userId() is not { } id) return;
        await _profiles.SetHealthEnabledAsync(id, false);
        Note = TurnedOffText;
        OfferText = null;
        StepsText = null;
    }

    /// <summary>Names what the user didn't let GainStreak save.</summary>
    private void CheckAccess()
    {
        if (!IsOn) { Problem = null; CanOpenSettings = false; return; }
        var off = new List<string>();
        if (_store.WriteAccess(HealthKind.StrengthWorkout) == HealthAccess.Denied) off.Add("Workouts");
        if (_store.WriteAccess(HealthKind.Weight) == HealthAccess.Denied) off.Add(off.Count == 0 ? "Weigh-ins" : "weigh-ins");
        Problem = off.Count == 0 ? null
            : $"{string.Join(" and ", off)} are off for GainStreak in {Texts.Store}, so they aren't saved there.";
        CanOpenSettings = off.Count > 0;
    }

    private void SetQuietly(bool on)
    {
        _loading = true;
        IsOn = on;
        _loading = false;
    }

    [RelayCommand] private void OpenSettings() => _store.OpenSettings();
}
