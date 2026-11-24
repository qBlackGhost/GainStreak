using System;
using System.Linq;
using System.Security.Cryptography;

namespace GymApp.Social;

/// <summary>
/// Invite codes for adding friends by QR code or link (Phase 21). One code per user, random, never the user id, so a
/// link can't be guessed by counting. Resetting gives a new code and the old one stops working.
/// </summary>
public static class FriendInvites
{
    public const int CodeLength = 10;

    /// <summary>Lowercase letters and digits without look-alikes (0/o, 1/l/i): about 49 bits in 10 characters.</summary>
    public const string Alphabet = "23456789abcdefghjkmnpqrstuvwxyz";

    /// <summary>The path of an invite link: <c>https://&lt;host&gt;/add/&lt;code&gt;</c>.</summary>
    public const string LinkPath = "/add/";

    public static string NewCode() => RandomNumberGenerator.GetString(Alphabet, CodeLength);

    /// <summary>The code in its stored form (lowercase), or null when it can't be a code. Checked before any lookup.</summary>
    public static string? Normalize(string? code)
    {
        if (code is null) return null;
        code = code.Trim().ToLowerInvariant();
        return code.Length == CodeLength && code.All(c => Alphabet.Contains(c)) ? code : null;
    }
}

/// <summary>The user's own invite code, or why there isn't one. <see cref="Url"/> is the link the server gives (null from
/// the rules alone, as in the TestBench's local mode). <see cref="IsStale"/>: the saved one, the server couldn't be reached.</summary>
public record InviteCodeResult(bool Success, string? Code = null, string? Error = null, string? Url = null, bool IsStale = false);

/// <summary>Who an invite code belongs to and how they relate to you, for the confirm card; or why it can't be used.</summary>
public record InviteLookup(bool Success, SocialUser? Inviter = null, FriendRelation Relation = FriendRelation.None, string? Error = null);

/// <summary>Invite links: <c>https://&lt;host&gt;/add/&lt;code&gt;</c>. The server hands out the link; the app only reads
/// the code back out of one the phone opened (the OS passes on links only for the app's verified domain).</summary>
public static class InviteLink
{
    /// <summary>The links' home. Used only where no server gives one: the TestBench's local mode.</summary>
    public const string DefaultBaseUrl = "https://gainstreak.app";

    /// <summary>The app's own scheme. The landing page's "Open in GainStreak" opens <c>gainstreak://add/&lt;code&gt;</c>,
    /// for in-app browsers (Instagram and the like) that never hand the https link to the OS.</summary>
    public const string AppScheme = "gainstreak";

    /// <summary>The (normalized) code in an invite link (<c>https://&lt;host&gt;/add/&lt;code&gt;</c> or
    /// <c>gainstreak://add/&lt;code&gt;</c>), or null for any other link. A trailing slash or a query is fine.</summary>
    public static string? TryParse(Uri? link)
    {
        if (link is not { IsAbsoluteUri: true }) return null;
        string path;
        if (link.Scheme.Equals(AppScheme, StringComparison.OrdinalIgnoreCase))
        {
            if (!link.Host.Equals("add", StringComparison.OrdinalIgnoreCase)) return null;
            path = "/add" + link.AbsolutePath.TrimEnd('/');
        }
        else if (link.Scheme == Uri.UriSchemeHttps || link.Scheme == Uri.UriSchemeHttp) path = link.AbsolutePath.TrimEnd('/');
        else return null;
        return path.StartsWith(FriendInvites.LinkPath, StringComparison.OrdinalIgnoreCase)
            ? FriendInvites.Normalize(path[FriendInvites.LinkPath.Length..])
            : null;
    }

    /// <summary>The app-scheme link for a code: <c>gainstreak://add/&lt;code&gt;</c>.</summary>
    public static string AppLinkFor(string code) => $"{AppScheme}:/{FriendInvites.LinkPath}{code}";

    /// <summary>The link for a code: the server's when it gave one, else on <see cref="DefaultBaseUrl"/>.</summary>
    public static string For(InviteCodeResult invite) => invite.Url ?? $"{DefaultBaseUrl}{FriendInvites.LinkPath}{invite.Code}";
}
