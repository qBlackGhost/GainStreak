using Foundation;
using UIKit;
using GymApp.App;
using GymApp.Client;

namespace GymApp.iOS;

/// <summary>Library/Application Support/GymApp for the data (in the phone's backup, but not shown in the Files app the
/// way Documents is) and the Keychain for the token.</summary>
public sealed class IosPlatform() : PhonePlatform(BuiltSettings.ServerUrl)
{
    public override string DataDirectory { get; } = Path.Combine(
        NSFileManager.DefaultManager.GetUrls(NSSearchPathDirectory.ApplicationSupportDirectory, NSSearchPathDomain.User)[0].Path!,
        "GymApp");

    public override ITokenStore CreateTokenStore() => new KeychainTokenStore();

    /// <summary>Only once the App Store listing exists (<c>GymAppAppStoreId</c>, 16h).</summary>
    public override bool CanOpenStorePage => BuiltSettings.AppStoreId.Length > 0;

    public override void OpenStorePage() =>
        UIApplication.SharedApplication.OpenUrl(new NSUrl($"itms-apps://apps.apple.com/app/id{BuiltSettings.AppStoreId}"),
            new UIApplicationOpenUrlOptions(), null);
}
