namespace GymApp.Contracts;

public sealed record RegisterRequest(string Username, string Email, string Password, string DeviceName);

/// <summary><see cref="Login"/> is the username or the email; anything with an '@' is looked up as an email.</summary>
public sealed record LoginRequest(string Login, string Password, string DeviceName);

/// <summary>Returned by register and login. Send <see cref="Token"/> as <c>Authorization: Bearer &lt;token&gt;</c>.</summary>
public sealed record AuthResponse(string Token, MeResponse User);

/// <summary><see cref="ShareGymCheckIns"/> is null until the user has been asked. <see cref="UsernameChangeAllowedAt"/>
/// (UTC, 0.28.0) is set only while a rename has to wait; older servers never send it.</summary>
public sealed record MeResponse(int Id, string Username, string Email, bool? ShareGymCheckIns, string? Avatar = null,
    bool EmailConfirmed = false, DateTime? UsernameChangeAllowedAt = null);

/// <summary><c>PUT /v1/me/username</c> (0.28.0). Same rules as register; the current password is asked for, like any
/// account change. Answers with the new <see cref="MeResponse"/>. A 429 inside the waiting time carries the date as
/// the <c>usernameChangeAllowedAt</c> extension of the ProblemDetails.</summary>
public sealed record ChangeUsernameRequest(string Username, string CurrentPassword);

/// <summary>Replaces the avatar: a colour, optionally with an icon ("violet" or "violet:flame"), or null for the plain initials.</summary>
public sealed record AvatarRequest(string? Avatar);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>Same email rules as register; the current password is asked for, like any account change.</summary>
public sealed record ChangeEmailRequest(string CurrentPassword, string Email);

public sealed record DeleteAccountRequest(string Password);

/// <summary>The code from the "confirm your email" message.</summary>
public sealed record ConfirmEmailRequest(string Code);

/// <summary>Answers 204 whether or not an account has that email, so the form can't be used to find accounts.</summary>
public sealed record PasswordResetRequest(string Email);

public sealed record PasswordResetConfirmRequest(string Email, string Code, string NewPassword);

/// <summary>The messages the emailed codes share between the app and the server.</summary>
public static class EmailCodeRules
{
    public const int Length = 6;

    public const string WrongCodeMessage = "That code isn't right, or it has expired.";
    public const string TooSoonMessage = "We just sent a code. Wait a minute before asking for another.";
    public const string NotSentMessage = "We couldn't send the email. Try again later.";
    public const string AlreadyConfirmedMessage = "Your email is already confirmed.";
}

/// <summary>The form field a 400 or 409 is about, sent as the <c>field</c> extension of the ProblemDetails
/// so the app can show the message under the right box. Absent when the error isn't about one field.</summary>
public static class ProblemFields
{
    public const string Username = "username";
    public const string Email = "email";
    public const string Password = "password";
    public const string NewPassword = "newPassword";
}

/// <summary>Extensions of a ProblemDetails besides <see cref="ProblemFields"/> (0.28.1). Old apps ignore them.</summary>
public static class ProblemExtensions
{
    /// <summary>On a 429 from a cooldown (feedback, reports): when the next try works, UTC.</summary>
    public const string RetryAt = "retryAt";
}
