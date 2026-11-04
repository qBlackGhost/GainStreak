using Android.Content;
using Uri = Android.Net.Uri;
using GymApp.App;
using GymApp.Client;

namespace GymApp.Android;

/// <summary>The app's private files folder (files/GymApp, never visible to other apps) and a Keystore-encrypted token.</summary>
public sealed class AndroidPlatform(Context context) : PhonePlatform(BuiltSettings.ServerUrl)
{
    public override string DataDirectory { get; } = Path.Combine(context.FilesDir!.AbsolutePath, "GymApp");

    public override ITokenStore CreateTokenStore() => new KeystoreTokenStore(Path.Combine(DataDirectory, "session.token"));

    public override INotificationScheduler CreateNotificationScheduler() => new AndroidNotificationScheduler(context);

    public override ILocationProvider CreateLocationProvider() => new AndroidLocationProvider(context);

    public override bool CanOpenStorePage => true;

    /// <summary>The Play Store app at this app's page; the web page where there's no store app (an emulator).</summary>
    public override void OpenStorePage()
    {
        var id = context.PackageName;
        try { Open($"market://details?id={id}"); }
        catch (ActivityNotFoundException) { Open($"https://play.google.com/store/apps/details?id={id}"); }
    }

    private void Open(string url) =>
        context.StartActivity(new Intent(Intent.ActionView, Uri.Parse(url)).AddFlags(ActivityFlags.NewTask));
}
