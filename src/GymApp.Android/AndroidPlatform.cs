using Android.Content;
using GymApp.App;
using GymApp.Client;

namespace GymApp.Android;

/// <summary>The app's private files folder (files/GymApp, never visible to other apps) and a Keystore-encrypted token.</summary>
public sealed class AndroidPlatform(Context context) : PhonePlatform(BuiltSettings.ServerUrl)
{
    public override string DataDirectory { get; } = Path.Combine(context.FilesDir!.AbsolutePath, "GymApp");

    public override ITokenStore CreateTokenStore() => new KeystoreTokenStore(Path.Combine(DataDirectory, "session.token"));
}
