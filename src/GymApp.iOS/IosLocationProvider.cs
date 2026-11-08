using CoreLocation;
using Foundation;
using GymApp.App;
using GymApp.Core;
using UIKit;

namespace GymApp.iOS;

/// <summary>"Verified at gym" (18c): one fix at a time from <c>CLLocationManager</c>, when in use only
/// (<c>NSLocationWhenInUseUsageDescription</c> in Info.plist). With precise location off iOS gives an approximate fix
/// with a large accuracy, which <see cref="GymProximity"/> then ignores.</summary>
public sealed class IosLocationProvider : ILocationProvider
{
    private CLLocationManager? _manager;
    private TaskCompletionSource<bool>? _permission;
    private TaskCompletionSource<LocationFix?>? _fix;

    // Created on first use, on the UI thread (the view models call from there), and kept: its delegate events need it alive
    private CLLocationManager Manager
    {
        get
        {
            if (_manager is not null) return _manager;
            _manager = new CLLocationManager { DesiredAccuracy = CLLocation.AccuracyNearestTenMeters };
            // iOS 14 and later answer through DidChangeAuthorization; the older AuthorizationChanged may no longer come
            _manager.DidChangeAuthorization += (_, _) => _permission?.TrySetResult(HasPermission);
            _manager.AuthorizationChanged += (_, _) => _permission?.TrySetResult(HasPermission);
            _manager.LocationsUpdated += (_, e) =>
            {
                var last = e.Locations.LastOrDefault();
                _fix?.TrySetResult(last is null || last.HorizontalAccuracy < 0
                    ? null
                    : new LocationFix(last.Coordinate.Latitude, last.Coordinate.Longitude, last.HorizontalAccuracy));
            };
            _manager.Failed += (_, _) => _fix?.TrySetResult(null);
            return _manager;
        }
    }

    public bool IsSupported => true;

    public bool HasPermission => Manager.AuthorizationStatus is CLAuthorizationStatus.AuthorizedWhenInUse or CLAuthorizationStatus.AuthorizedAlways;

    public bool IsPrecise => HasPermission && Manager.AccuracyAuthorization == CLAccuracyAuthorization.FullAccuracy;

    public bool NeedsNoteBeforePrompt => false;

    public async Task<bool> RequestPermissionAsync()
    {
        if (Manager.AuthorizationStatus != CLAuthorizationStatus.NotDetermined) return HasPermission;
        _permission = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Manager.RequestWhenInUseAuthorization();
        // Wait for a real answer: the delegate can also fire with "not determined" before the user answers, and if no
        // callback comes at all the status is read again every half second (0.18.1: without that, the first Start
        // workout never opened the workout after the location prompt)
        while (Manager.AuthorizationStatus == CLAuthorizationStatus.NotDetermined)
        {
            await Task.WhenAny(_permission.Task, Task.Delay(PermissionPoll));
            if (_permission.Task.IsCompleted)
                _permission = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        return HasPermission;
    }

    private static readonly TimeSpan PermissionPoll = TimeSpan.FromMilliseconds(500);

    public async Task<LocationFix?> GetFixAsync(TimeSpan timeout)
    {
        if (!HasPermission) return null;
        _fix?.TrySetResult(null);
        var fix = _fix = new TaskCompletionSource<LocationFix?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Manager.RequestLocation();
        var done = await Task.WhenAny(fix.Task, Task.Delay(timeout));
        if (done != fix.Task) { Manager.StopUpdatingLocation(); fix.TrySetResult(null); }
        return await fix.Task;
    }

    public void OpenSettings() =>
        UIApplication.SharedApplication.OpenUrl(new NSUrl(UIApplication.OpenSettingsUrlString), new UIApplicationOpenUrlOptions(), null);
}
