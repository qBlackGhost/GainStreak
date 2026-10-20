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

    Task<AccountResult> ChangeEmailAsync(string newEmail, string currentPassword);
    Task<AccountResult> ChangePasswordAsync(string currentPassword, string newPassword);

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

/// <summary>An account from before Phase 14 that can be moved online. <see cref="NeedsEmail"/>: it has none, and the server needs one.</summary>
public sealed record MoveOnlineOffer(int LocalUserId, string Username, string? Email)
{
    public bool NeedsEmail => Email is null;
}
