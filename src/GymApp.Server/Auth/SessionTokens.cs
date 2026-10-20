using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using GymApp.Server.Data;

namespace GymApp.Server.Auth;

/// <summary>Opaque login tokens: 32 random bytes, base64url, stored only as their SHA-256.
/// A session lives 90 days since it was last used.</summary>
public static class SessionTokens
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(90);

    /// <summary>How often a used session's expiry is pushed forward. Less often would let it lapse; more often is a write per request.</summary>
    public static readonly TimeSpan SlideEvery = TimeSpan.FromHours(1);

    public const string SessionIdClaim = "sid";

    public static string NewToken() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>Creates and adds (doesn't save) a session for the user; returns the token to hand to the client.</summary>
    public static string Start(ServerDbContext db, int userId, string? deviceName, DateTime now)
    {
        var token = NewToken();
        var name = (deviceName ?? "").Trim();
        db.Sessions.Add(new Session
        {
            UserId = userId,
            TokenHash = Hash(token),
            DeviceName = name.Length > Session.MaxDeviceNameLength ? name[..Session.MaxDeviceNameLength] : name,
            CreatedAt = now,
            LastUsedAt = now,
            ExpiresAt = now + Lifetime
        });
        return token;
    }

    public static int UserId(this ClaimsPrincipal user) => int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static long SessionId(this ClaimsPrincipal user) => long.Parse(user.FindFirstValue(SessionIdClaim)!);

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
