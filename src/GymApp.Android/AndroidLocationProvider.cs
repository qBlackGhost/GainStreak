using Android;
using Android.Content;
using Android.Content.PM;
using Android.Locations;
using Android.OS;
using GymApp.App;
using GymApp.Core;
using Uri = Android.Net.Uri;

namespace GymApp.Android;

/// <summary>"Verified at gym" (18c): one fix at a time from the platform's <see cref="LocationManager"/> (no Google Play
/// Services), only while the app is in use; no background location permission. The fused provider where there is one
/// (Android 12 and later), else GPS with precise location, else the network.</summary>
public sealed class AndroidLocationProvider(Context context) : ILocationProvider
{
    public bool IsSupported => true;

    public bool HasPermission => Allowed(context);

    public bool IsPrecise => context.CheckSelfPermission(Manifest.Permission.AccessFineLocation) == Permission.Granted;

    public bool NeedsNoteBeforePrompt => true;

    public static bool Allowed(Context context) =>
        context.CheckSelfPermission(Manifest.Permission.AccessFineLocation) == Permission.Granted
        || context.CheckSelfPermission(Manifest.Permission.AccessCoarseLocation) == Permission.Granted;

    public Task<bool> RequestPermissionAsync() => MainActivity.RequestLocationPermissionAsync(context);

    public async Task<LocationFix?> GetFixAsync(TimeSpan timeout)
    {
        if (!HasPermission) return null;
        var manager = (LocationManager)context.GetSystemService(Context.LocationService)!;
        if (Provider(manager) is not { } provider) return null;

        var result = new TaskCompletionSource<Location?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancel = new CancellationSignal();
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
            manager.GetCurrentLocation(provider, cancel, context.MainExecutor!, new Consumer(l => result.TrySetResult(l)));
        else
            RequestSingle(manager, provider, result);

        var done = await Task.WhenAny(result.Task, Task.Delay(timeout));
        if (done != result.Task) { cancel.Cancel(); result.TrySetResult(null); }
        var location = await result.Task;
        return location is null || !location.HasAccuracy ? null
            : new LocationFix(location.Latitude, location.Longitude, location.Accuracy);
    }

    private string? Provider(LocationManager manager)
    {
        bool On(string p) => manager.AllProviders.Contains(p) && manager.IsProviderEnabled(p);
        if (OperatingSystem.IsAndroidVersionAtLeast(31) && On(LocationManager.FusedProvider)) return LocationManager.FusedProvider;
        var fine = context.CheckSelfPermission(Manifest.Permission.AccessFineLocation) == Permission.Granted;
        if (fine && On(LocationManager.GpsProvider)) return LocationManager.GpsProvider;
        return On(LocationManager.NetworkProvider) ? LocationManager.NetworkProvider : null;
    }

#pragma warning disable CA1422 // the single update is the only one-shot request before Android 11
    private static void RequestSingle(LocationManager manager, string provider, TaskCompletionSource<Location?> result) =>
        manager.RequestSingleUpdate(provider, new Listener(l => result.TrySetResult(l)), Looper.MainLooper);
#pragma warning restore CA1422

    public void OpenSettings() =>
        context.StartActivity(new Intent(global::Android.Provider.Settings.ActionApplicationDetailsSettings,
            Uri.Parse($"package:{context.PackageName}")).AddFlags(ActivityFlags.NewTask));

    private sealed class Consumer(Action<Location?> got) : Java.Lang.Object, Java.Util.Functions.IConsumer
    {
        public void Accept(Java.Lang.Object? t) => got(t as Location);
    }

    private sealed class Listener(Action<Location?> got) : Java.Lang.Object, ILocationListener
    {
        public void OnLocationChanged(Location location) => got(location);
        public void OnProviderDisabled(string provider) => got(null);
        public void OnProviderEnabled(string provider) { }
        public void OnStatusChanged(string? provider, Availability status, Bundle? extras) { }
    }
}
