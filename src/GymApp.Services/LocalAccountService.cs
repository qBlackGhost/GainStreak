using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Data;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

/// <summary>
/// Accounts on this computer only, for the TestBench's local mode (its moving clock is something the server refuses).
/// The behaviour from before Phase 14: <see cref="AuthService"/> for the checks and <see cref="RememberMeService"/> to
/// stay signed in (always, like the server mode). There is never anything to move online.
/// </summary>
public class LocalAccountService(
    AuthService auth, RememberMeService remember, SessionService session, IDbContextFactory<AppDbContext> dbFactory) : IAccountService
{
    public async Task<AccountResult> RegisterAsync(string username, string email, string password) =>
        SignedIn(await auth.RegisterAsync(username, email, password), newAccount: true);

    public async Task<AccountResult> SignInAsync(string login, string password) =>
        SignedIn(await auth.LoginAsync(login, password));

    public Task<AccountResult> MoveOnlineAsync(int localUserId, string password, string? email = null, string? username = null) =>
        Task.FromResult(AccountResult.Fail("Accounts stay on this computer in local mode."));

    public Task<bool> RestoreAsync() => Task.FromResult(remember.TryRestore());

    public Task LogOutAsync()
    {
        remember.Forget();
        session.SignOut();
        return Task.CompletedTask;
    }

    public async Task<AccountResult> ChangeEmailAsync(string newEmail, string currentPassword)
    {
        if (session.CurrentUser is not { } current) return AccountResult.Fail("Sign in first.");
        var r = await auth.ChangeEmailAsync(current.Id, newEmail, currentPassword);
        if (!r.Success) return AccountResult.Fail(r.Error!, r.Field);
        current.Email = r.User!.Email;
        return AccountResult.Ok(r.User);
    }

    public async Task<AccountResult> ChangePasswordAsync(string currentPassword, string newPassword)
    {
        if (session.CurrentUser is not { } current) return AccountResult.Fail("Sign in first.");
        var r = await auth.ChangePasswordAsync(current.Id, currentPassword, newPassword);
        if (!r.Success) return AccountResult.Fail(r.Error!, r.Field);
        current.PasswordHash = r.User!.PasswordHash;
        return AccountResult.Ok(r.User);
    }

    public async Task<AccountResult> DeleteAccountAsync(string password)
    {
        if (session.CurrentUser is not { } current) return AccountResult.Fail("Sign in first.");
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var hash = await db.Users.Where(u => u.Id == current.Id).Select(u => u.PasswordHash).FirstOrDefaultAsync();
            if (hash is null || !PasswordHasher.Verify(password, hash))
                return AccountResult.Fail("That password is not right.", FormField.Password);
            remember.Forget();
            await LocalAccounts.DeleteUserAsync(db, current.Id);
        }
        session.SignOut();
        return new AccountResult(true);
    }

    private AccountResult SignedIn(AuthResult r, bool newAccount = false)
    {
        if (!r.Success) return AccountResult.Fail(r.Error!, r.Field);
        remember.Remember(r.User!, true);   // before SignIn, which navigates
        session.SignIn(r.User!, newAccount: newAccount);
        return AccountResult.Ok(r.User!);
    }
}
