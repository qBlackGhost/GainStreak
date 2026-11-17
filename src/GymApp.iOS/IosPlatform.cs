using Foundation;
using UIKit;
using GymApp.App;
using GymApp.Client;
using GymApp.Services;

namespace GymApp.iOS;

/// <summary>Library/Application Support/GymApp for the data (in the phone's backup, but not shown in the Files app the
/// way Documents is) and the Keychain for the token.</summary>
public sealed class IosPlatform() : PhonePlatform(BuiltSettings.ServerUrl)
{
    public override string DataDirectory { get; } = Path.Combine(
        NSFileManager.DefaultManager.GetUrls(NSSearchPathDirectory.ApplicationSupportDirectory, NSSearchPathDomain.User)[0].Path!,
        "GymApp");

    public override ITokenStore CreateTokenStore() => new KeychainTokenStore();

    public override INotificationScheduler CreateNotificationScheduler() => new IosNotificationScheduler();

    public override ILocationProvider CreateLocationProvider() => new IosLocationProvider();

    public override ILiveActivity CreateLiveActivity() => new IosLiveActivity();

    public override IHealthStore CreateHealthStore() => new IosHealthStore();   // Apple Health (Phase 20)

    public override IPushNotifications CreatePushNotifications() => new IosPushNotifications();   // friend notifications (Phase 22)

    /// <summary>Only once the App Store listing exists (<c>GymAppAppStoreId</c>, 16h).</summary>
    public override bool CanOpenStorePage => BuiltSettings.AppStoreId.Length > 0;

    public override void OpenStorePage() =>
        UIApplication.SharedApplication.OpenUrl(new NSUrl($"itms-apps://apps.apple.com/app/id{BuiltSettings.AppStoreId}"),
            new UIApplicationOpenUrlOptions(), null);

    /// <summary>The system's share sheet over the app's window. On an iPad it hangs from the middle of the screen.</summary>
    public override void Share(string text)
    {
        var root = UIApplication.SharedApplication.ConnectedScenes.OfType<UIWindowScene>()
            .SelectMany(s => s.Windows).FirstOrDefault(w => w.IsKeyWindow)?.RootViewController;
        if (root is null) return;
        while (root.PresentedViewController is { } shown) root = shown;
        var sheet = new UIActivityViewController([new NSString(text)], null);
        if (sheet.PopoverPresentationController is { } popover)
        {
            popover.SourceView = root.View;
            popover.SourceRect = new CoreGraphics.CGRect(root.View!.Bounds.GetMidX(), root.View.Bounds.GetMidY(), 0, 0);
            popover.PermittedArrowDirections = 0;
        }
        root.PresentViewController(sheet, true, null);
    }
}
