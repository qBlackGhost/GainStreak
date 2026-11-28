using System;
using System.Threading.Tasks;
using GymApp.Data.Models;

namespace GymApp.Services;

/// <summary>
/// Signing up, in and out, and changing the account. The app uses <see cref="AccountService"/> (the social server is the
/// real login); the TestBench's local mode uses <see cref="LocalAccountService"/> (accounts on this computer only).
/// Everything but <see cref="RestoreAsync"/> and <see cref="LogOutAsync"/> reports problems in the result, never by throwing
/// for an expected failure.
/// </summary>
public interface IAccountService
{
    Task<AccountResult> RegisterAsync(string username, string email, string password);

    /// <summary><paramref name="login"/> is the username or the email.</summary>
    Task<AccountResult> SignInAsync(string login, string password);

    /// <summary>Puts an account from before Phase 14 (see <see cref="AccountResult.MoveOnline"/>) on the server and links
    /// it. <paramref name="email"/> and <paramref name="username"/> replace the local ones when given (no email yet, or the
    /// username is taken on the server).</summary>
    Task<AccountResult> MoveOnlineAsync(int localUserId, string password, string? email = null, string? username = null);

    /// <summary>At startup: signs in with the stored credential if there is one. True if someone is signed in.</summary>
    Task<bool> RestoreAsync();

    Task LogOutAsync();

    /// <summary>Whether the signed-in account's email is confirmed; null when unknown (offline, signed out, or local
    /// accounts, which have no email to confirm).</summary>
    bool? EmailConfirmed { get; }

    /// <summary>Emails another code to the signed-in account.</summary>
    Task<AccountResult> SendConfirmCodeAsync();

    Task<AccountResult> ConfirmEmailAsync(string code);

    /// <summary>Emails a code if an account has that address. Succeeds either way, so it can't be used to find accounts.</summary>
    Task<AccountResult> RequestPasswordResetAsync(string email);

    /// <summary>Sets a new password with the emailed code. Every device has to sign in again.</summary>
    Task<AccountResult> ResetPasswordAsync(string email, string code, string newPassword);

    Task<AccountResult> ChangeEmailAsync(string newEmail, string currentPassword);
    Task<AccountResult> ChangePasswordAsync(string currentPassword, string newPassword);

    /// <summary>Renames the signed-in user (Phase 28): the registration rules, the current password, and once every
    /// <see cref="Core.Credentials.UsernameChangeCooldown"/> (a change of capital letters only is free). Needs the
    /// server; offline it fails and nothing is queued. A success updates the session's user and raises
    /// <see cref="SessionService.UserUpdated"/>.</summary>
    Task<AccountResult> ChangeUsernameAsync(string newUsername, string currentPassword);

    /// <summary>When the next rename works (UTC), while it has to wait; null when it works now or isn't known
    /// (offline since sign-in, or signed out).</summary>
    DateTime? UsernameChangeAllowedAt { get; }

    /// <summary>Asks the server, when the rename screen opens, whether it can be reached and when a rename works
    /// again, so the screen can say so before the user tries. Updates <see cref="UsernameChangeAllowedAt"/>.</summary>
    Task<UsernameChangeCheck> CheckUsernameChangeAsync();

    /// <summary>Deletes the account and every local row of it, then signs out.</summary>
    Task<AccountResult> DeleteAccountAsync(string password);
}

/// <summary>The outcome of an account action. <see cref="Field"/> is a <see cref="FormField"/> when the error belongs to one box.</summary>
public sealed record AccountResult(bool Success, string? Error = null, string? Field = null)
{
    public User? User { get; init; }

    /// <summary>Signed in from the cached password: the server couldn't be reached.</summary>
    public bool SignedInOffline { get; init; }

    /// <summary>Set (with Success false) when the login matches an account on this computer that isn't online yet.</summary>
    public MoveOnlineOffer? MoveOnline { get; init; }

    public static AccountResult Ok(User user, bool offline = false) => new(true) { User = user, SignedInOffline = offline };
    public static AccountResult Fail(string error, string? field = null) => new(false, error, field);
}

/// <summary>Whether a rename can reach the server now, and when the next one works (UTC; null: now).</summary>
public sealed record UsernameChangeCheck(bool Online, DateTime? AllowedAt);

/// <summary>An account from before Phase 14 that can be moved online. <see cref="NeedsEmail"/>: it has none, and the server needs one.</summary>
public sealed record MoveOnlineOffer(int LocalUserId, string Username, string? Email)
{
    public bool NeedsEmail => Email is null;
}
