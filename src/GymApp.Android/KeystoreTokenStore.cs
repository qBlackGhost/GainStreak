using System.Text;
using Android.Security.Keystore;
using GymApp.Client;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;

namespace GymApp.Android;

/// <summary>
/// The session token in a file in the app's private folder, encrypted (AES-GCM) with a key that lives in the Android
/// Keystore and never leaves it. A backup restored onto another phone brings the file but not the key, so the token
/// can't be read there: <see cref="Get"/> then returns null and the user signs in again.
/// </summary>
public sealed class KeystoreTokenStore(string path) : ITokenStore
{
    private const string KeyStoreName = "AndroidKeyStore";
    private const string Alias = "gainstreak-session";
    private const string Transformation = "AES/GCM/NoPadding";
    private const int TagBits = 128;

    private readonly Lock _lock = new();

    public string? Get()
    {
        lock (_lock)
        {
            if (!File.Exists(path)) return null;
            try
            {
                var data = File.ReadAllBytes(path);
                var ivLength = data[0];
                var cipher = Cipher.GetInstance(Transformation)!;
                cipher.Init(CipherMode.DecryptMode, GetOrCreateKey(), new GCMParameterSpec(TagBits, data, 1, ivLength));
                return Encoding.UTF8.GetString(cipher.DoFinal(data, 1 + ivLength, data.Length - 1 - ivLength)!);
            }
            catch (Exception)
            {
                // A file from another phone or a lost key: unreadable, so start signed out
                File.Delete(path);
                return null;
            }
        }
    }

    public void Set(string token)
    {
        lock (_lock)
        {
            var cipher = Cipher.GetInstance(Transformation)!;
            cipher.Init(CipherMode.EncryptMode, GetOrCreateKey());
            var iv = cipher.GetIV()!;
            var sealedToken = cipher.DoFinal(Encoding.UTF8.GetBytes(token))!;

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            using (var file = File.Create(temp))
            {
                file.WriteByte((byte)iv.Length);
                file.Write(iv);
                file.Write(sealedToken);
            }
            File.Move(temp, path, overwrite: true);
        }
    }

    public void Clear()
    {
        lock (_lock) File.Delete(path);
    }

    private static IKey GetOrCreateKey()
    {
        var store = KeyStore.GetInstance(KeyStoreName)!;
        store.Load(null);
        if (store.GetKey(Alias, null) is { } key) return key;

        var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, KeyStoreName)!;
        generator.Init(new KeyGenParameterSpec.Builder(Alias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
            .SetKeySize(256)
            .Build());
        return generator.GenerateKey()!;
    }
}
