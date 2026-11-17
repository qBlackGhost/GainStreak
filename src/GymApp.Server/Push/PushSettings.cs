using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GymApp.Server.Push;

/// <summary>APNs token auth: the .p8 key (its PEM text), its id and the team id, and the app's bundle id (the topic).</summary>
public sealed record ApnsSettings(string KeyId, string TeamId, string PrivateKeyPem, string BundleId);

/// <summary>FCM HTTP v1: the service account key file's JSON and the project it belongs to.</summary>
public sealed record FcmSettings(string ServiceAccountJson, string ProjectId);

/// <summary>
/// Settings for push notifications (Phase 22), all optional, from the <c>Push</c> section (<c>Push__Apns__KeyId</c> and so
/// on in production; the two secrets come from Secret Manager):
/// <list type="bullet">
/// <item><c>Apns:KeyId</c>, <c>Apns:TeamId</c>, <c>Apns:PrivateKey</c> (the .p8 file's text): all three or none. Turn on
/// iPhone pushes. <c>Apns:BundleId</c> defaults to <c>com.gainstreak.app</c>.</item>
/// <item><c>Fcm:ServiceAccountJson</c> (the service account key file's text): turns on Android pushes.</item>
/// </list>
/// Unset, tokens are still stored and nothing is sent. A value that's set but malformed is a startup error; the message
/// names the setting, never its value.
/// </summary>
public sealed partial record PushSettings(ApnsSettings? Apns, FcmSettings? Fcm)
{
    public const string SectionName = "Push";
    public const string DefaultBundleId = "com.gainstreak.app";

    public static PushSettings Read(IConfiguration config)
    {
        var s = config.GetSection(SectionName);
        return new PushSettings(ReadApns(s.GetSection("Apns")), ReadFcm(s.GetSection("Fcm")));
    }

    private static ApnsSettings? ReadApns(IConfigurationSection s)
    {
        var keyId = Value(s["KeyId"]);
        var teamId = Value(s["TeamId"]);
        var key = Value(s["PrivateKey"]);
        if (keyId is null && teamId is null && key is null) return null;
        if (keyId is null || teamId is null || key is null)
            throw new InvalidOperationException("Set all of Push__Apns__KeyId, Push__Apns__TeamId and Push__Apns__PrivateKey, or none.");
        if (!AppleIdPattern().IsMatch(keyId))
            throw new InvalidOperationException("Push__Apns__KeyId must be the 10-character key id from the Apple developer account.");
        if (!AppleIdPattern().IsMatch(teamId))
            throw new InvalidOperationException("Push__Apns__TeamId must be the 10-character Apple Team ID.");
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(key);
            if (ecdsa.KeySize != 256) throw new CryptographicException();
            ecdsa.SignData([1], HashAlgorithmName.SHA256);   // a public key alone would import but can't sign
        }
        catch (Exception e) when (e is CryptographicException or ArgumentException)
        {
            throw new InvalidOperationException("Push__Apns__PrivateKey must be the text of the APNs .p8 key file (a P-256 private key).");
        }
        return new ApnsSettings(keyId, teamId, key, Value(s["BundleId"]) ?? DefaultBundleId);
    }

    private static FcmSettings? ReadFcm(IConfigurationSection s)
    {
        if (Value(s["ServiceAccountJson"]) is not { } json) return null;
        const string error = "Push__Fcm__ServiceAccountJson must be the text of a Firebase service account key file (JSON).";
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string? Text(string name) => root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            if (Text("type") != "service_account" || Text("project_id") is not { Length: > 0 } project
                || Text("client_email") is null || Text("private_key") is null)
                throw new InvalidOperationException(error);
            // Parses the private key too, so a damaged one stops startup here rather than at the first push. Google's
            // own message isn't passed on: it could quote the file
            try { _ = Google.Apis.Auth.OAuth2.CredentialFactory.FromJson<Google.Apis.Auth.OAuth2.ServiceAccountCredential>(json); }
            catch (Exception) { throw new InvalidOperationException(error); }
            return new FcmSettings(json, project);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(error);
        }
    }

    private static string? Value(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    [GeneratedRegex("^[A-Z0-9]{10}$")]
    private static partial Regex AppleIdPattern();
}
