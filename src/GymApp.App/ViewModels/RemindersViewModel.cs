using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using GymApp.Services;

namespace GymApp.App.ViewModels;

/// <summary>Profile → Reminders (16g), on the phones only. Off until the user turns it on, which asks for permission
/// to notify; refused, the switch goes back off and says where to allow it. Each change is saved and the reminders
/// replanned at once.</summary>
public partial class RemindersViewModel : ObservableObject
{
    public const string RefusedText =
        "Notifications are off for GainStreak. Allow them in your phone's Settings, then turn Reminders on again.";

    private readonly ReminderService _reminders;
    private readonly INotificationScheduler _notifications;
    private readonly ReminderSync _sync;
    private readonly Func<int?> _userId;
    private bool _loading;

    public RemindersViewModel(ReminderService reminders, INotificationScheduler notifications, ReminderSync sync, Func<int?> userId)
    {
        _reminders = reminders;
        _notifications = notifications;
        _sync = sync;
        _userId = userId;
    }

    /// <summary>False on the desktop: the section is hidden.</summary>
    public bool IsSupported => _notifications.IsSupported;

    [ObservableProperty] public partial bool IsOn { get; set; }
    [ObservableProperty] public partial bool TrainingOn { get; set; } = true;
    [ObservableProperty] public partial TimeSpan? TrainingTime { get; set; } = ReminderSettings.DefaultTrainingTime.ToTimeSpan();
    [ObservableProperty] public partial bool MissedOn { get; set; } = true;

    /// <summary>Why the switch turned itself back off, or null.</summary>
    [ObservableProperty] public partial string? Note { get; set; }

    public async Task LoadAsync()
    {
        if (!IsSupported || _userId() is not { } id) return;
        var s = await _reminders.GetSettingsAsync(id);
        _loading = true;
        IsOn = s.Enabled;
        TrainingOn = s.TrainingDay;
        TrainingTime = s.TrainingTime.ToTimeSpan();
        MissedOn = s.MissedDay;
        _loading = false;
    }

    partial void OnIsOnChanged(bool value)
    {
        if (_loading) return;
        _ = value ? TurnOnAsync() : SaveAsync();
    }

    partial void OnTrainingOnChanged(bool value) { if (!_loading) _ = SaveAsync(); }
    partial void OnTrainingTimeChanged(TimeSpan? value) { if (!_loading) _ = SaveAsync(); }
    partial void OnMissedOnChanged(bool value) { if (!_loading) _ = SaveAsync(); }

    private async Task TurnOnAsync()
    {
        Note = null;
        if (!await _notifications.RequestPermissionAsync())
        {
            _loading = true;
            IsOn = false;
            _loading = false;
            Note = RefusedText;
            return;
        }
        await SaveAsync();
    }

    private async Task SaveAsync()
    {
        if (_userId() is not { } id) return;
        var time = TrainingTime is { } t ? TimeOnly.FromTimeSpan(t) : ReminderSettings.DefaultTrainingTime;
        await _reminders.SaveSettingsAsync(id, new ReminderSettings(IsOn, TrainingOn, time, MissedOn));
        await _sync.RefreshAsync();
    }
}
