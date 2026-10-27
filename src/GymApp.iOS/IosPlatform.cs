using Foundation;
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
}
