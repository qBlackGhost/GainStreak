namespace GymApp.Server.Data;

/// <summary>A server account. Ids come from the server's own sequence, so they're unique across every phone.</summary>
public class User
{
    public int Id { get; set; }

    /// <summary>As typed at registration (trimmed), for display.</summary>
    public string Username { get; set; } = "";

    /// <summary>Lowercase <see cref="Username"/>; unique, so usernames are unique ignoring case.</summary>
    public string UsernameNormalized { get; set; } = "";

    /// <summary>Stored normalized (<see cref="Core.Credentials.NormalizeEmail"/>); unique.</summary>
    public string Email { get; set; } = "";

    /// <summary>When a code sent to <see cref="Email"/> came back; null until then, and again after the email changes.
    /// Nothing is held back from an unconfirmed account: the app only reminds.</summary>
    public DateTime? EmailConfirmedAt { get; set; }

    public string PasswordHash { get; set; } = "";

    /// <summary>Null until the user has been asked.</summary>
    public bool? ShareGymCheckIns { get; set; }

    /// <summary>The user's choice in <see cref="Core.AvatarChoice"/> form; null for the plain initials.</summary>
    public string? Avatar { get; set; }

    /// <summary>The preset the user trains with (the app's preset id), from the last stats upload. Null with
    /// <see cref="UsesCustomSplit"/> false: no split chosen, or never uploaded.</summary>
    public int? SplitPresetId { get; set; }

    public bool UsesCustomSplit { get; set; }

    public const int MaxFeaturedBadgeLength = 48;

    /// <summary>The badge shown next to the name (Phase 25), as <c>"&lt;id&gt;:&lt;tier&gt;"</c>; null for none or never
    /// uploaded. Shown to friends only while <see cref="ShareBadges"/> is on.</summary>
    public string? FeaturedBadge { get; set; }

    /// <summary>Whether friends may see the user's badges and featured badge (Phase 25). On until the phone says
    /// otherwise.</summary>
    public bool ShareBadges { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    /// <summary>When the user last renamed themselves (Phase 28); null if never. A change of capital letters only
    /// doesn't set it. The next rename works <see cref="Core.Credentials.UsernameChangeCooldown"/> after it.</summary>
    public DateTime? UsernameChangedAt { get; set; }

    /// <summary>Set by the <c>suspend</c> command (<see cref="AccountCommands"/>). A suspended account can't sign in and
    /// doesn't show to anyone, but keeps its data, so <c>unsuspend</c> puts everything back.</summary>
    public DateTime? SuspendedAt { get; set; }

    public static string NormalizeUsername(string username) => username.Trim().ToLowerInvariant();
}

/// <summary>A signed-in device. Only the SHA-256 of the token is stored, so a database leak doesn't hand out logins.</summary>
public class Session
{
    public const int MaxDeviceNameLength = 64;

    public long Id { get; set; }
    public int UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime LastUsedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
