using Foundation;
using GymApp.Client;
using Security;

namespace GymApp.iOS;

/// <summary>
/// The session token in the iOS Keychain, readable after the phone's first unlock and only on this phone: it stays out
/// of backups and never moves to another device, where the user signs in again.
/// </summary>
public sealed class KeychainTokenStore : ITokenStore
{
    private const string Service = "com.gainstreak.app";
    private const string Account = "session";

    private static SecRecord Query() => new(SecKind.GenericPassword) { Service = Service, Account = Account };

    public string? Get()
    {
        var match = SecKeyChain.QueryAsRecord(Query(), out var status);
        return status == SecStatusCode.Success ? match?.ValueData?.ToString(NSStringEncoding.UTF8) : null;
    }

    public void Set(string token)
    {
        SecKeyChain.Remove(Query());
        var status = SecKeyChain.Add(new SecRecord(SecKind.GenericPassword)
        {
            Service = Service,
            Account = Account,
            ValueData = NSData.FromString(token, NSStringEncoding.UTF8),
            Accessible = SecAccessible.AfterFirstUnlockThisDeviceOnly,
        });
        if (status != SecStatusCode.Success) throw new InvalidOperationException($"The Keychain refused the session token ({status}).");
    }

    public void Clear() => SecKeyChain.Remove(Query());
}
