using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using GymApp.Client;
using GymApp.Contracts;
using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

/// <summary>
/// Accounts on the social server, which is the real login; the local <see cref="User"/> row is its cache, linked by
/// <see cref="User.ServerUserId"/>, so every local table keeps pointing at the local id and no workout data moves.
/// The local row also caches the email and a hash of the password, so this phone's own account can sign in offline.
/// The signed-in row is marked with a hash of the session token (in <see cref="User.RememberTokenHash"/>), so a restart
/// with no connection knows whose token it holds.
/// </summary>
public class AccountService(
    SocialApiClient api, IDbContextFactory<AppDbContext> dbFactory, ITokenStore tokens, SessionService session,
    string? deviceName = null) : IAccountService
{
    public const string WrongLogin = "Wrong username or password.";
    public const string SessionEndedNotice = "Your session ended. Sign in again.";
    public const string NeedsEmailMessage = "Add an email to move your account online.";

    private readonly string _device = deviceName ?? Environment.MachineName;

    public async Task<AccountResult> RegisterAsync(string username, string email, string password)
    {
        username = username.Trim();
        email = Credentials.NormalizeEmail(email);
        if (CheckNew(username, email, password) is { } invalid) return invalid;

        // An account from before Phase 14 on this computer should be moved online, not registered next to itself
        await using (var db = await dbFactory.CreateDbContextAsync())
            if (await db.Users.AnyAsync(u => u.ServerUserId == null && (u.Username == username || u.Email == email)))
                return AccountResult.Fail("This computer already has that account. Sign in with it to move it online.", FormField.Username);

        var r = await api.RegisterAsync(new RegisterRequest(username, email, password, _device));
        if (!r.IsOk) return Failed(r);
        var user = await SaveLinkedAsync(r.Value!.User, password, localUserId: null, r.Value.Token);
        return SignedIn(user, r.Value.Token, newAccount: true);
    }

    public async Task<AccountResult> SignInAsync(string login, string password)
    {
        login = login.Trim();
        var r = await api.LoginAsync(new LoginRequest(login, password, _device));

        if (r.IsOk)
        {
            var me = r.Value!.User;
            await using var db = await dbFactory.CreateDbContextAsync();
            // No linked row yet, but this computer's account from before Phase 14 has the same login and password:
            // it's the same person, so their workouts join the server account instead of starting a new local user
            var localId = await db.Users.Where(u => u.ServerUserId == me.Id).Select(u => (int?)u.Id).FirstOrDefaultAsync()
                ?? (await FindUnlinkedAsync(db, login, password))?.Id;
            var user = await SaveLinkedAsync(me, password, localId, r.Value.Token);
            return SignedIn(user, r.Value.Token);
        }

        // Offline, or this build is too old for the server: this phone's own account still signs in from its cache,
        // so workouts can be logged; friends show cached data until there's a connection (or an update)
        if (r.IsOffline || r.IsUpdateRequired)
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var cached = await FindLocalAsync(db, login, linked: true);
            if (cached is null || !PasswordHasher.Verify(password, cached.PasswordHash))
                return AccountResult.Fail(cached is null ? r.Error! : WrongLogin);
            // Too old a build isn't offline: the update banner says why friends are stale, not "you're offline"
            session.SignIn(cached, offline: r.IsOffline);
            return AccountResult.Ok(cached, offline: r.IsOffline);
        }

        // Refused. A 401 is a wrong login, and never falls back to the cache; it may be an account not online yet
        if (r.Status == 401)
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            if (await FindUnlinkedAsync(db, login, password) is { } local)
                return new AccountResult(false) { MoveOnline = new MoveOnlineOffer(local.Id, local.Username, local.Email) };
            return AccountResult.Fail(WrongLogin);
        }
        return Failed(r);
    }

    public async Task<AccountResult> MoveOnlineAsync(int localUserId, string password, string? email = null, string? username = null)
    {
        User local;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var found = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == localUserId && u.ServerUserId == null);
            if (found is null || !PasswordHasher.Verify(password, found.PasswordHash)) return AccountResult.Fail(WrongLogin);
            local = found;
        }

        var name = (username ?? local.Username).Trim();
        var mail = email is null ? local.Email : Credentials.NormalizeEmail(email);
        if (mail is null) return AccountResult.Fail(NeedsEmailMessage, FormField.Email);
        if (Credentials.CheckUsername(name) is { } nameError) return AccountResult.Fail(nameError, FormField.Username);
        if (Credentials.CheckEmail(mail) is { } emailError) return AccountResult.Fail(emailError, FormField.Email);

        // Old local passwords may break today's rules; the server checks, and its message goes under the password
        var r = await api.RegisterAsync(new RegisterRequest(name, mail, password, _device));
        if (!r.IsOk) return Failed(r);
        var user = await SaveLinkedAsync(r.Value!.User, password, localUserId, r.Value.Token);
        return SignedIn(user, r.Value.Token);
    }

    public async Task<bool> RestoreAsync()
    {
        if (tokens.Get() is not { } token) return false;
        var r = await api.GetMeAsync();
        if (r.IsOk)
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var user = await db.Users.FirstOrDefaultAsync(u => u.ServerUserId == r.Value!.Id);
            if (user is null)
            {
                // The token outlived this computer's copy of the account (deleted locally): sign in again to rebuild it
                tokens.Clear();
                return false;
            }
            await RefreshAsync(db, user, r.Value!, password: null);
            user.RememberTokenHash = TokenHash(token);
            await db.SaveChangesAsync();
            session.SignIn(user);
            return true;
        }
        // Offline, or too old a build (the token may still be good: keep it for after the update)
        if (r.IsOffline || r.IsUpdateRequired)
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var hash = TokenHash(token);
            var cached = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.ServerUserId != null && u.RememberTokenHash == hash);
            if (cached is null) return false;
            session.SignIn(cached, offline: r.IsOffline);
            return true;
        }
        // 401: expired or revoked. Anything else: the token is no good either way
        tokens.Clear();
        return false;
    }

    public async Task LogOutAsync()
    {
        // Best effort: offline, the server's session just runs out on its own
        if (tokens.Get() is not null) await api.LogoutAsync();
        await ForgetAsync(clearCache: true);
        session.SignOut();
    }

    /// <summary>The server said the token is no longer valid (the client's SessionExpired). Signs out to the login screen.</summary>
    public async Task EndSessionAsync()
    {
        if (!session.IsLoggedIn) return;
        await ForgetAsync(clearCache: false);
        session.SignOut(SessionEndedNotice);
    }

    public async Task<AccountResult> ChangeEmailAsync(string newEmail, string currentPassword)
    {
        if (session.CurrentUser is not { } current) return AccountResult.Fail("Sign in first.");
        var email = Credentials.NormalizeEmail(newEmail);
        if (Credentials.CheckEmail(email) is { } emailError) return AccountResult.Fail(emailError, FormField.Email);

        var r = await api.ChangeEmailAsync(new ChangeEmailRequest(currentPassword, email));
        if (!r.IsOk) return Failed(r);

        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.FirstAsync(u => u.Id == current.Id);
        await FreeLocalNamesAsync(db, user.Username, email, user.Id);
        user.Email = email;
        await db.SaveChangesAsync();
        current.Email = email;
        return AccountResult.Ok(user);
    }

    public async Task<AccountResult> ChangePasswordAsync(string currentPassword, string newPassword)
    {
        if (session.CurrentUser is not { } current) return AccountResult.Fail("Sign in first.");
        if (Credentials.CheckPassword(newPassword) is { } passwordError) return AccountResult.Fail(passwordError, FormField.NewPassword);

        var r = await api.ChangePasswordAsync(new ChangePasswordRequest(currentPassword, newPassword));
        if (!r.IsOk) return Failed(r);

        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.FirstAsync(u => u.Id == current.Id);
        user.PasswordHash = PasswordHasher.Hash(newPassword);
        await db.SaveChangesAsync();
        current.PasswordHash = user.PasswordHash;
        return AccountResult.Ok(user);
    }

    public async Task<AccountResult> DeleteAccountAsync(string password)
    {
        if (session.CurrentUser is not { } current) return AccountResult.Fail("Sign in first.");
        var r = await api.DeleteAccountAsync(new DeleteAccountRequest(password));
        if (r.IsOffline) return AccountResult.Fail("Deleting your account needs a connection. Try again when you're online.");
        if (!r.IsOk) return Failed(r);

        // Only once the server has deleted it: otherwise a failure would leave an online account with no local data
        await using (var db = await dbFactory.CreateDbContextAsync())
            await LocalAccounts.DeleteUserAsync(db, current.Id);
        tokens.Clear();
        session.SignOut();
        return new AccountResult(true);
    }

    // --- Helpers ---

    private AccountResult SignedIn(User user, string token, bool newAccount = false)
    {
        tokens.Set(token);
        session.SignIn(user, newAccount: newAccount);
        return AccountResult.Ok(user);
    }

    /// <summary>Clears the token and this computer's mark of it; with <paramref name="clearCache"/>, the cached friend data too.
    /// Workouts stay.</summary>
    private async Task ForgetAsync(bool clearCache)
    {
        tokens.Clear();
        if (session.CurrentUser is not { } user) return;
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.RememberTokenHash, (string?)null));
        if (clearCache) await db.SocialCache.Where(c => c.UserId == user.Id).ExecuteDeleteAsync();
    }

    /// <summary>Creates or updates the local row for a server account (the row <paramref name="localUserId"/> if given),
    /// caching its username, email, password hash and sharing setting, and marks it as the holder of <paramref name="token"/>.</summary>
    private async Task<User> SaveLinkedAsync(MeResponse me, string password, int? localUserId, string token)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var user = localUserId is { } id ? await db.Users.FirstAsync(u => u.Id == id) : null;
        if (user is null)
        {
            user = new User();
            db.Users.Add(user);
        }
        user.ServerUserId = me.Id;
        user.RememberTokenHash = TokenHash(token);
        await RefreshAsync(db, user, me, password);
        await db.SaveChangesAsync();
        return user;
    }

    /// <summary>Copies the server's view of the account onto the local row (not saved).</summary>
    private static async Task RefreshAsync(AppDbContext db, User user, MeResponse me, string? password)
    {
        await FreeLocalNamesAsync(db, me.Username, me.Email, user.Id);
        user.Username = me.Username;
        user.Email = me.Email;
        if (password is not null && !PasswordHasher.Verify(password, user.PasswordHash))
            user.PasswordHash = PasswordHasher.Hash(password);

        // A sharing change still waiting to upload is newer than what the server says
        var pending = user.Id != 0 && await db.SyncStates.AnyAsync(s => s.UserId == user.Id && s.SharingDirtySince != null);
        if (!pending && user.Id != 0 && await ProfileService.ProfileForWriteAsync(db, user.Id) is { } profile)
            profile.ShareGymCheckIns = me.ShareGymCheckIns;
        else if (user.Id == 0 && me.ShareGymCheckIns is not null)
            db.UserProfiles.Add(new UserProfile { User = user, ShareGymCheckIns = me.ShareGymCheckIns });
    }

    /// <summary>
    /// Usernames and emails are unique locally too, but the server decides who owns one now. Another local row holding
    /// it is out of date (an account deleted on the server, then the name taken by someone else) or an account from
    /// before Phase 14: it keeps its data and gets a free name ("alice.12") and no email.
    /// </summary>
    private static async Task FreeLocalNamesAsync(AppDbContext db, string username, string? email, int keepUserId)
    {
        var others = await db.Users
            .Where(u => u.Id != keepUserId && (u.Username == username || (email != null && u.Email == email)))
            .ToListAsync();
        foreach (var other in others)
        {
            if (string.Equals(other.Username, username, StringComparison.OrdinalIgnoreCase))
            {
                var suffix = $".{other.Id}";
                other.Username = other.Username[..Math.Min(other.Username.Length, Credentials.MaxUsernameLength - suffix.Length)] + suffix;
            }
            if (email is not null && other.Email == email) other.Email = null;
        }
        if (others.Count > 0) await db.SaveChangesAsync();
    }

    /// <summary>The local row the login names (by email if it has an '@'), linked or not.</summary>
    private static Task<User?> FindLocalAsync(AppDbContext db, string login, bool linked)
    {
        var users = db.Users.AsNoTracking().Where(u => (u.ServerUserId != null) == linked);
        if (login.Contains('@'))
        {
            var email = Credentials.NormalizeEmail(login);
            return users.FirstOrDefaultAsync(u => u.Email == email);
        }
        return users.FirstOrDefaultAsync(u => u.Username == login);   // NOCASE
    }

    private static async Task<User?> FindUnlinkedAsync(AppDbContext db, string login, string password) =>
        await FindLocalAsync(db, login, linked: false) is { } u && PasswordHasher.Verify(password, u.PasswordHash) ? u : null;

    private static AccountResult? CheckNew(string username, string email, string password)
    {
        if (Credentials.CheckUsername(username) is { } nameError) return AccountResult.Fail(nameError, FormField.Username);
        if (Credentials.CheckEmail(email) is { } emailError) return AccountResult.Fail(emailError, FormField.Email);
        if (Credentials.CheckPassword(password) is { } passwordError) return AccountResult.Fail(passwordError, FormField.Password);
        return null;
    }

    /// <summary>A refusal or offline result as an account error, with the server's field mapped to the form's.</summary>
    private static AccountResult Failed(ApiResult r) => AccountResult.Fail(r.Error!, r.Field switch
    {
        ProblemFields.Username => FormField.Username,
        ProblemFields.Email => FormField.Email,
        ProblemFields.Password => FormField.Password,
        ProblemFields.NewPassword => FormField.NewPassword,
        _ => null
    });

    private static string TokenHash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
