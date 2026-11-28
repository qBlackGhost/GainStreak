using System;
using System.Collections.Generic;
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

    private const string NoServerMessage = "That needs the social server.";

    public bool? EmailConfirmed => null;
    public Task<AccountResult> SendConfirmCodeAsync() => Task.FromResult(AccountResult.Fail(NoServerMessage));
    public Task<AccountResult> ConfirmEmailAsync(string code) => Task.FromResult(AccountResult.Fail(NoServerMessage));
    public Task<AccountResult> RequestPasswordResetAsync(string email) => Task.FromResult(AccountResult.Fail(NoServerMessage));
    public Task<AccountResult> ResetPasswordAsync(string email, string code, string newPassword) => Task.FromResult(AccountResult.Fail(NoServerMessage));

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

    /// <summary>When each local user last renamed (UTC). Kept in memory only: local mode is the TestBench's, and a
    /// restart starting the wait over is fine there (Phase 28 adds no app table).</summary>
    private readonly Dictionary<int, DateTime> _renamedAt = [];

    private static DateTime UtcNow => AppClock.Now.ToUniversalTime();

    public DateTime? UsernameChangeAllowedAt =>
        session.CurrentUser is { } u && _renamedAt.TryGetValue(u.Id, out var at) && at + Credentials.UsernameChangeCooldown > UtcNow
            ? at + Credentials.UsernameChangeCooldown
            : null;

    public Task<UsernameChangeCheck> CheckUsernameChangeAsync() =>
        Task.FromResult(new UsernameChangeCheck(true, UsernameChangeAllowedAt));

    public async Task<AccountResult> ChangeUsernameAsync(string newUsername, string currentPassword)
    {
        if (session.CurrentUser is not { } current) return AccountResult.Fail("Sign in first.");
        var username = newUsername.Trim();
        // The same order as the server: the name, the password, then the waiting time
        if (Credentials.CheckUsername(username) is { } nameError) return AccountResult.Fail(nameError, FormField.Username);
        var capitalsOnly = string.Equals(username, current.Username, StringComparison.OrdinalIgnoreCase)
                           && username != current.Username;
        if (!capitalsOnly && UsernameChangeAllowedAt is { } allowedAt)
        {
            // Checked after the password, like the server
            await using var db = await dbFactory.CreateDbContextAsync();
            var hash = await db.Users.Where(u => u.Id == current.Id).Select(u => u.PasswordHash).FirstOrDefaultAsync();
            if (hash is null || !PasswordHasher.Verify(currentPassword, hash))
                return AccountResult.Fail("That password is not right.", FormField.Password);
            if (username != current.Username)
                return AccountResult.Fail(Credentials.UsernameChangeTooSoonMessage(allowedAt), FormField.Username);
        }

        var r = await auth.ChangeUsernameAsync(current.Id, username, currentPassword);
        if (!r.Success) return AccountResult.Fail(r.Error!, r.Field);
        if (!capitalsOnly) _renamedAt[current.Id] = UtcNow;
        current.Username = r.User!.Username;
        session.NotifyUserUpdated();
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
