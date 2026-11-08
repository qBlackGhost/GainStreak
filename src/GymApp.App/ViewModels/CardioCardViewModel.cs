using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Core;
using GymApp.Services;

namespace GymApp.App.ViewModels;

/// <summary>
/// The cardio timer on Home (0.18.2, phones): a ring that fills toward the next point, the time, Pause and Finish
/// cardio. When the workout was verified at a gym the phone's position is checked once a minute and whenever the app
/// comes back (<see cref="CheckNowAsync"/>); found away, the timer pauses and the card offers to save what was done.
/// Its view ticks <see cref="Tick"/> once a second; the service keeps the times, so the card can go and come back.
/// </summary>
public partial class CardioCardViewModel : ViewModelBase
{
    private readonly CardioService _service;
    private readonly ILocationProvider _location;
    private readonly int _userId;
    private readonly Func<CardioEndResult?, Task> _ended;
    private readonly Func<DateTime> _now;
    private DateTime _lastCheck;
    private bool _checking;

    /// <param name="ended">Finish, or the timer stopped by itself (null).</param>
    /// <param name="now">For tests; the app uses <see cref="AppClock"/>.</param>
    public CardioCardViewModel(CardioService service, ILocationProvider location, int userId, CardioInfo info,
        Func<CardioEndResult?, Task> ended, Func<DateTime>? now = null)
    {
        _service = service;
        _location = location;
        _userId = userId;
        _ended = ended;
        _now = now ?? (() => AppClock.Now);
        Info = info;
        _lastCheck = _now();
        Render();
    }

    public CardioInfo Info { get; private set; }

    [ObservableProperty] public partial string TimeText { get; set; } = "0:00";
    /// <summary>The ring, in degrees: how far toward the next point.</summary>
    [ObservableProperty] public partial double Sweep { get; set; }
    /// <summary>"+2 at 20:00", or "The most cardio earns" once at +3.</summary>
    [ObservableProperty] public partial string NextText { get; set; } = "";
    /// <summary>"+1 pt earned", or "No points yet".</summary>
    [ObservableProperty] public partial string EarnedText { get; set; } = "";
    [ObservableProperty] public partial bool HasEarned { get; set; }
    /// <summary>"At Iron Gym", or "Not checked at a gym".</summary>
    [ObservableProperty] public partial string PlaceText { get; set; } = "";
    [ObservableProperty] public partial bool IsChecked { get; set; }
    [ObservableProperty] public partial bool IsRunning { get; set; }
    [ObservableProperty] public partial bool IsPausedByUser { get; set; }
    [ObservableProperty] public partial bool HasLeftGym { get; set; }
    /// <summary>Running or paused by the user: Pause / Resume and Finish cardio. Left the gym: the save offer instead.</summary>
    public bool ShowControls => !HasLeftGym;
    [ObservableProperty] public partial string LeftText { get; set; } = "";
    /// <summary>"Save 14 min · +1", "Save 4 min", or "End cardio" under a minute.</summary>
    [ObservableProperty] public partial string SaveText { get; set; } = "";
    [ObservableProperty] public partial string? Error { get; set; }
    [ObservableProperty] public partial bool IsSaving { get; set; }

    partial void OnHasLeftGymChanged(bool value) => OnPropertyChanged(nameof(ShowControls));

    /// <summary>Once a second from the view: the time, and the position check when it's due.</summary>
    public void Tick()
    {
        Render();
        if (_now() - _lastCheck >= CardioRules.CheckEvery) _ = CheckNowAsync();
    }

    /// <summary>Checks the position now (the app came back to the front, or a minute went by). Also notices a timer
    /// that stopped by itself.</summary>
    public async Task CheckNowAsync()
    {
        if (_checking) return;
        _checking = true;
        _lastCheck = _now();
        try
        {
            CardioInfo? info;
            if (Info.IsChecked && _location is { IsSupported: true, HasPermission: true, IsPrecise: true }
                && await _location.GetFixAsync(LiveWorkoutCardViewModel.FixTimeout) is { } fix)
                info = await _service.CheckAsync(_userId, fix);
            else
                info = await _service.GetOpenAsync(_userId);
            if (info is null) { await _ended(null); return; }
            Info = info;
            Render();
        }
        catch (Exception) { /* tried again in a minute */ }
        finally { _checking = false; }
    }

    [RelayCommand]
    private async Task PauseAsync()
    {
        try { Apply(await _service.PauseAsync(_userId)); }
        catch (Exception) { Error = "Could not pause the timer."; }
    }

    [RelayCommand]
    private async Task ResumeAsync()
    {
        try
        {
            Apply(await _service.ResumeAsync(_userId));
            if (Info.IsChecked) _ = CheckNowAsync();   // still away: paused again straight away
        }
        catch (Exception) { Error = "Could not start the timer again."; }
    }

    [RelayCommand]
    private async Task FinishAsync()
    {
        IsSaving = true;
        Error = null;
        try
        {
            var r = await _service.FinishAsync(_userId);
            if (!r.Success) { Error = r.Error; return; }
            await _ended(r);
        }
        catch (Exception) { Error = "Could not save the cardio."; }
        finally { IsSaving = false; }
    }

    private void Apply(CardioInfo? info)
    {
        Error = null;
        if (info is null) { _ = _ended(null); return; }
        Info = info;
        Render();
    }

    private void Render()
    {
        var seconds = Info.Seconds(_now());
        var minutes = CardioRules.Minutes(seconds);
        var points = PointsCalculator.CardioPoints(minutes);
        TimeText = FormatTime(seconds);
        Sweep = 360 * CardioRules.ProgressToNextPoint(seconds);
        NextText = CardioRules.NextPointAt(minutes) is { } at ? $"+{PointsCalculator.CardioPoints(at)} at {at}:00" : "The most cardio earns";
        HasEarned = points > 0;
        EarnedText = points > 0 ? $"+{points} pt{(points == 1 ? "" : "s")} earned" : "No points yet";
        IsChecked = Info.IsChecked;
        PlaceText = Info.IsChecked ? $"At {Info.GymName}" : "Not checked at a gym";
        IsRunning = Info.IsRunning;
        IsPausedByUser = Info.Pause == CardioPause.User;
        HasLeftGym = Info.Pause == CardioPause.LeftGym;
        LeftText = $"The timer paused. Head back to keep going, or save the {minutes} minute{(minutes == 1 ? "" : "s")} you did.";
        SaveText = SaveLabel(minutes);
    }

    /// <summary>"14:07", or "1:02:05" from an hour.</summary>
    public static string FormatTime(int seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    public static string SaveLabel(int minutes)
    {
        if (minutes == 0) return "End cardio";
        var points = PointsCalculator.CardioPoints(minutes);
        return points > 0 ? $"Save {minutes} min · +{points}" : $"Save {minutes} min";
    }
}
