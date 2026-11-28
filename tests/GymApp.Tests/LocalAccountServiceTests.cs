using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Services;

namespace GymApp.Tests;

/// <summary>The TestBench's local mode: accounts on this computer only, as before Phase 14.</summary>
[Collection(DataDirectoryCollection.Name)]
public sealed class LocalAccountServiceTests : IDisposable
{
    private const string Pw = "correct horse 1";
    private readonly TestDb _db = new();
    private readonly SessionService _session = new();
    private readonly LocalAccountService _accounts;
    // RememberMeService writes its file in the data folder: keep it out of the real one
    private readonly TempDataDirectory _dataDir = new();

    public LocalAccountServiceTests()
    {
        _accounts = new LocalAccountService(new AuthService(_db), new RememberMeService(_db, _session), _session, _db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _dataDir.Dispose();
    }

    [Fact] public async Task Register_SignIn_ChangePassword_AndDelete_StayLocal()
    {
        var r = await _accounts.RegisterAsync("alice", "alice@example.com", Pw);
        Assert.True(r.Success, r.Error);
        Assert.Null(r.User!.ServerUserId);
        Assert.True(_session.IsNewAccount);

        Assert.Equal(FormField.Password, (await _accounts.ChangePasswordAsync("wrong password", "brand new pass")).Field);
        Assert.Equal(FormField.NewPassword, (await _accounts.ChangePasswordAsync(Pw, "short")).Field);
        Assert.True((await _accounts.ChangePasswordAsync(Pw, "brand new pass")).Success);

        await _accounts.LogOutAsync();
        Assert.False((await _accounts.SignInAsync("alice", Pw)).Success);
        Assert.True((await _accounts.SignInAsync("alice@example.com", "brand new pass")).Success);
        Assert.Null((await _accounts.SignInAsync("alice", Pw)).MoveOnline);   // never offered locally

        Assert.Equal(FormField.Password, (await _accounts.DeleteAccountAsync("wrong password")).Field);
        Assert.True((await _accounts.DeleteAccountAsync("brand new pass")).Success);
        Assert.False(_session.IsLoggedIn);
        using var db = _db.CreateDbContext();
        Assert.Empty(db.Users);
    }

    // --- Change username (Phase 28) ---

    [Fact] public async Task ChangeUsername_FollowsTheServersRules_AndWaits30Days()
    {
        var shift = AppClock.Offset;
        try
        {
            await _accounts.RegisterAsync("alice", "alice@example.com", Pw);
            await _accounts.RegisterAsync("bobby", "bobby@example.com", Pw);
            await _accounts.SignInAsync("alice", Pw);
            var updates = 0;
            _session.UserUpdated += () => updates++;

            Assert.Equal(FormField.Username, (await _accounts.ChangeUsernameAsync("al ice", Pw)).Field);
            Assert.Equal(FormField.Username, (await _accounts.ChangeUsernameAsync("gainstreak_admin", Pw)).Field);
            Assert.Equal(FormField.Password, (await _accounts.ChangeUsernameAsync("alice2", "wrong password")).Field);
            Assert.Equal(("That username is taken.", FormField.Username),
                ((await _accounts.ChangeUsernameAsync("BOBBY", Pw)).Error, (await _accounts.ChangeUsernameAsync("BOBBY", Pw)).Field));
            Assert.Equal(FormField.Username, (await _accounts.ChangeUsernameAsync("alice", Pw)).Field);   // already yours
            Assert.Null(_accounts.UsernameChangeAllowedAt);

            // Capitals only: free, and doesn't start the wait
            Assert.True((await _accounts.ChangeUsernameAsync("Alice", Pw)).Success);
            Assert.Null(_accounts.UsernameChangeAllowedAt);

            Assert.True((await _accounts.ChangeUsernameAsync("alice2", Pw)).Success);
            Assert.Equal(("alice2", 2), (_session.CurrentUser!.Username, updates));
            Assert.NotNull(_accounts.UsernameChangeAllowedAt);

            var tooSoon = await _accounts.ChangeUsernameAsync("alice3", Pw);
            Assert.Equal(FormField.Username, tooSoon.Field);
            Assert.StartsWith("You can change your username again on ", tooSoon.Error);
            Assert.Equal(FormField.Password, (await _accounts.ChangeUsernameAsync("alice3", "wrong password")).Field);
            Assert.True((await _accounts.ChangeUsernameAsync("Alice2", Pw)).Success);   // capitals still free

            AppClock.Offset = shift + Credentials.UsernameChangeCooldown + TimeSpan.FromMinutes(1);
            Assert.Null(_accounts.UsernameChangeAllowedAt);
            Assert.True((await _accounts.ChangeUsernameAsync("alice3", Pw)).Success);

            // The new name signs in, the old one doesn't, and the old one is free
            await _accounts.LogOutAsync();
            Assert.False((await _accounts.SignInAsync("alice", Pw)).Success);
            Assert.True((await _accounts.SignInAsync("alice3", Pw)).Success);
            await _accounts.LogOutAsync();
            Assert.True((await _accounts.RegisterAsync("alice", "other@example.com", Pw)).Success);
        }
        finally
        {
            AppClock.Offset = shift;
        }
    }
}
