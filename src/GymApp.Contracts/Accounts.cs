namespace GymApp.Contracts;

public sealed record RegisterRequest(string Username, string Email, string Password, string DeviceName);

/// <summary><see cref="Login"/> is the username or the email; anything with an '@' is looked up as an email.</summary>
public sealed record LoginRequest(string Login, string Password, string DeviceName);

/// <summary>Returned by register and login. Send <see cref="Token"/> as <c>Authorization: Bearer &lt;token&gt;</c>.</summary>
public sealed record AuthResponse(string Token, MeResponse User);

/// <summary><see cref="ShareGymCheckIns"/> is null until the user has been asked.</summary>
public sealed record MeResponse(int Id, string Username, string Email, bool? ShareGymCheckIns, string? Avatar = null);

/// <summary>Replaces the avatar: a colour, optionally with an icon ("violet" or "violet:flame"), or null for the plain initials.</summary>
public sealed record AvatarRequest(string? Avatar);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>Same email rules as register; the current password is asked for, like any account change.</summary>
public sealed record ChangeEmailRequest(string CurrentPassword, string Email);

public sealed record DeleteAccountRequest(string Password);

/// <summary>The form field a 400 or 409 is about, sent as the <c>field</c> extension of the ProblemDetails
/// so the app can show the message under the right box. Absent when the error isn't about one field.</summary>
public static class ProblemFields
{
    public const string Username = "username";
    public const string Email = "email";
    public const string Password = "password";
    public const string NewPassword = "newPassword";
}
