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
}
