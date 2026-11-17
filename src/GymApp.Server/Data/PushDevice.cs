using GymApp.Contracts;

namespace GymApp.Server.Data;

/// <summary>A signed-in phone's push token (Phase 22). One per session, so deleting the session (logout, a password
/// change or reset, account deletion) deletes the token with it. A token is unique: registered by a newer session, it
/// moves there, so a phone never gets the previous account's notifications.</summary>
public class PushDevice
{
    public long Id { get; set; }
    public long SessionId { get; set; }

    /// <summary>The session's user, kept here for the lookup by receiver.</summary>
    public int UserId { get; set; }

    public PushPlatform Platform { get; set; }
    public string Token { get; set; } = "";

    /// <summary>APNs' sandbox (development builds) rather than production. Always false for FCM.</summary>
    public bool Sandbox { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
