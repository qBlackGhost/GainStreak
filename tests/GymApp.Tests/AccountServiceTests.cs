using System.Net;
using System.Net.Http.Json;
using GymApp.Contracts;
using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Services;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Tests;

public sealed class AccountServiceTests : IDisposable
{
    private const string Pw = "correct horse 1";
    private static readonly MeResponse Alice = new(41, "alice", "alice@example.com", null);

    private readonly TestDb _db = new();
    private readonly FakeSocialServer _server = new();
    private readonly SessionService _session = new();
    private readonly AccountService _accounts;

    public AccountServiceTests() => _accounts = new AccountService(_server.Api, _db, _server.Tokens, _session, "test phone");

    public void Dispose() => _db.Dispose();

    /// <summary>A local row as Phase 14 leaves it: linked, with the cached email and password hash.</summary>
    private int AddLinked(MeResponse me, string password = Pw)
    {
        using var db = _db.CreateDbContext();
        var user = new User { Username = me.Username, Email = me.Email, PasswordHash = PasswordHasher.Hash(password), ServerUserId = me.Id };
        db.Users.Add(user);
        db.SaveChanges();
        return user.Id;
    }

    /// <summary>An account from before Phase 14: on this computer only.</summary>
    private int AddUnlinked(string username, string? email, string password = Pw, int points = 0)
    {
        using var db = _db.CreateDbContext();
        var user = new User { Username = username, Email = email, PasswordHash = PasswordHasher.Hash(password), Points = points };
        db.Users.Add(user);
        db.SaveChanges();
        return user.Id;
    }

    private void AddWorkout(int userId, DateOnly date)
    {
        using var db = _db.CreateDbContext();
        db.WorkoutLogs.Add(new WorkoutLog { UserId = userId, Date = date });
        db.SaveChanges();
    }

    private User Local(int id)
    {
        using var db = _db.CreateDbContext();
        return db.Users.AsNoTracking().Single(u => u.Id == id);
    }

    // --- Register ---

    [Fact] public async Task Register_CreatesTheLinkedLocalUser_AndSignsIn()
    {
        _server.Accepts(Alice);

        var r = await _accounts.RegisterAsync(" alice ", "Alice@Example.com", Pw);

        Assert.True(r.Success, r.Error);
        var local = Local(r.User!.Id);
        Assert.Equal((41, "alice", "alice@example.com"), (local.ServerUserId, local.Username, local.Email));
        Assert.True(PasswordHasher.Verify(Pw, local.PasswordHash));
        Assert.Equal("token-1", _server.Tokens.Get());
        Assert.Equal(local.Id, _session.CurrentUser!.Id);
        Assert.False(_session.SignedInOffline);
    }

    [Fact] public async Task Register_TakenUsername_IsAFieldError_AndCreatesNothing()
    {
        _server.On("POST v1/auth/register", HttpStatusCode.Conflict, FakeSocialServer.Problem(409, "That username is taken.", "username"));

        var r = await _accounts.RegisterAsync("alice", "alice@example.com", Pw);

        Assert.Equal(("That username is taken.", FormField.Username), (r.Error, r.Field));
        using var db = _db.CreateDbContext();
        Assert.Empty(db.Users);
        Assert.Null(_server.Tokens.Get());
        Assert.False(_session.IsLoggedIn);
    }

    [Fact] public async Task Register_Offline_SaysSo()
    {
        _server.Offline = true;
        var r = await _accounts.RegisterAsync("alice", "alice@example.com", Pw);
        Assert.Equal("You're offline. Try again when you're connected.", r.Error);
    }

    [Fact] public async Task Register_ForAnAccountAlreadyOnThisComputer_AsksToSignIn()
    {
        AddUnlinked("alice", null);
        _server.Accepts(Alice);

        var r = await _accounts.RegisterAsync("ALICE", "alice@example.com", Pw);

        Assert.False(r.Success);
        Assert.Equal(FormField.Username, r.Field);
        Assert.DoesNotContain("POST v1/auth/register", _server.Calls);
    }

    // --- Sign in ---

    [Fact] public async Task SignIn_WithAnExistingLink_ReusesTheLocalUserAndTheirData()
    {
        var id = AddLinked(Alice with { Username = "Alice" });
        AddWorkout(id, new DateOnly(2026, 10, 18));
        _server.Accepts(Alice with { ShareGymCheckIns = true });

        var r = await _accounts.SignInAsync("alice", Pw);

        Assert.True(r.Success, r.Error);
        Assert.Equal(id, r.User!.Id);
        Assert.Equal("alice", Local(id).Username);   // the server's spelling wins
        using var db = _db.CreateDbContext();
        Assert.Single(db.Users);
        Assert.Equal(id, db.WorkoutLogs.Single().UserId);
        Assert.True(db.UserProfiles.Single(p => p.UserId == id).ShareGymCheckIns);
    }

    [Fact] public async Task SignIn_OnANewComputer_CreatesTheLocalUser()
    {
        _server.Accepts(Alice with { ShareGymCheckIns = false });

        var r = await _accounts.SignInAsync("alice@example.com", Pw);

        Assert.True(r.Success, r.Error);
        var local = Local(r.User!.Id);
        Assert.Equal((41, "alice"), (local.ServerUserId, local.Username));
        Assert.True(PasswordHasher.Verify(Pw, local.PasswordHash));
        using var db = _db.CreateDbContext();
        Assert.False(db.UserProfiles.Single().ShareGymCheckIns);
    }

    [Fact] public async Task SignIn_NewPasswordFromAnotherDevice_UpdatesTheCachedHash()
    {
        var id = AddLinked(Alice, "old password 1");
        _server.Accepts(Alice);

        await _accounts.SignInAsync("alice", Pw);

        Assert.True(PasswordHasher.Verify(Pw, Local(id).PasswordHash));
    }

    [Fact] public async Task SignIn_KeepsAPendingSharingChange()
    {
        var id = AddLinked(Alice);
        using (var db = _db.CreateDbContext())
        {
            db.UserProfiles.Add(new UserProfile { UserId = id, ShareGymCheckIns = true });
            db.SyncStates.Add(new SyncState { UserId = id, SharingDirtySince = DateTime.UtcNow });
            db.SaveChanges();
        }
        _server.Accepts(Alice with { ShareGymCheckIns = false });

        await _accounts.SignInAsync("alice", Pw);

        using var check = _db.CreateDbContext();
        Assert.True(check.UserProfiles.Single().ShareGymCheckIns);
    }

    [Fact] public async Task SignIn_Offline_WithTheRightCachedPassword_SignsInOffline()
    {
        var id = AddLinked(Alice);
        _server.Offline = true;

        var r = await _accounts.SignInAsync("Alice", Pw);

        Assert.True(r.Success);
        Assert.True(r.SignedInOffline);
        Assert.True(_session.SignedInOffline);
        Assert.Equal(id, _session.CurrentUser!.Id);
    }

    [Fact] public async Task SignIn_Offline_WithAWrongPassword_Fails()
    {
        AddLinked(Alice);
        _server.Offline = true;

        var r = await _accounts.SignInAsync("alice", "wrong password");

        Assert.Equal(AccountService.WrongLogin, r.Error);
        Assert.False(_session.IsLoggedIn);
    }

    [Fact] public async Task SignIn_Offline_WithNoLinkedAccount_SaysOffline()
    {
        AddUnlinked("alice", null);   // not online yet: moving it needs a connection
        _server.Offline = true;

        var r = await _accounts.SignInAsync("alice", Pw);

        Assert.Equal("You're offline. Try again when you're connected.", r.Error);
        Assert.Null(r.MoveOnline);
    }

    [Fact] public async Task SignIn_WrongPerTheServer_NeverFallsBackToTheCache()
    {
        AddLinked(Alice);   // the cached hash matches, but the password was changed on another device
        _server.RefusesLogin();

        var r = await _accounts.SignInAsync("alice", Pw);

        Assert.Equal(AccountService.WrongLogin, r.Error);
        Assert.False(_session.IsLoggedIn);
    }

    [Fact] public async Task SignIn_ToAnAccountNotOnlineYet_OffersToMoveIt()
    {
        var id = AddUnlinked("bob", null);
        _server.RefusesLogin();

        var r = await _accounts.SignInAsync("BOB", Pw);

        Assert.False(r.Success);
        Assert.Equal(new MoveOnlineOffer(id, "bob", null), r.MoveOnline);
        Assert.True(r.MoveOnline!.NeedsEmail);

        var wrong = await _accounts.SignInAsync("bob", "wrong password");
        Assert.Null(wrong.MoveOnline);
        Assert.Equal(AccountService.WrongLogin, wrong.Error);
    }

    [Fact] public async Task SignIn_ServerAccountWithTheSameLoginAndPassword_LinksTheLocalAccount()
    {
        var id = AddUnlinked("alice", null, points: 300);
        AddWorkout(id, new DateOnly(2026, 10, 18));
        _server.Accepts(Alice);

        var r = await _accounts.SignInAsync("alice", Pw);

        Assert.Equal(id, r.User!.Id);
        Assert.Equal((41, 300, "alice@example.com"), (Local(id).ServerUserId, Local(id).Points, Local(id).Email));
        using var db = _db.CreateDbContext();
        Assert.Single(db.Users);
    }

    [Fact] public async Task SignIn_NameHeldByAnotherLocalRow_FreesIt_KeepingThatRowsData()
    {
        // A stale row: its server account was deleted elsewhere, and someone else registered "alice" since
        var stale = AddLinked(new MeResponse(7, "alice", "alice@example.com", null), "other password 1");
        AddWorkout(stale, new DateOnly(2026, 10, 1));
        _server.Accepts(Alice);

        var r = await _accounts.SignInAsync("alice", Pw);

        Assert.True(r.Success, r.Error);
        Assert.NotEqual(stale, r.User!.Id);
        Assert.Equal(($"alice.{stale}", (string?)null), (Local(stale).Username, Local(stale).Email));
        using var db = _db.CreateDbContext();
        Assert.Equal(stale, db.WorkoutLogs.Single().UserId);
    }

    // --- Move online ---

    [Fact] public async Task MoveOnline_LinksTheAccount_AndKeepsItsWorkoutsAndPoints()
    {
        var id = AddUnlinked("bob", null, points: 520);
        AddWorkout(id, new DateOnly(2026, 10, 18));
        RegisterRequest? sent = null;
        _server.On("POST v1/auth/register", req =>
        {
            sent = req.Content!.ReadFromJsonAsync<RegisterRequest>(ApiFormat.Json).Result;
            return FakeSocialServer.Respond(HttpStatusCode.Created, new AuthResponse("token-2", new MeResponse(9, "bob", "bob@example.com", null)));
        });

        var needsEmail = await _accounts.MoveOnlineAsync(id, Pw);
        Assert.Equal((AccountService.NeedsEmailMessage, FormField.Email), (needsEmail.Error, needsEmail.Field));

        var r = await _accounts.MoveOnlineAsync(id, Pw, email: "Bob@Example.com");

        Assert.True(r.Success, r.Error);
        Assert.Equal(new RegisterRequest("bob", "bob@example.com", Pw, "test phone"), sent);
        var local = Local(id);
        Assert.Equal((9, "bob@example.com", 520), (local.ServerUserId, local.Email, local.Points));
        Assert.Equal("token-2", _server.Tokens.Get());
        Assert.Equal(id, _session.CurrentUser!.Id);
        using var db = _db.CreateDbContext();
        Assert.Equal(id, db.WorkoutLogs.Single().UserId);
    }

    [Fact] public async Task MoveOnline_TakenUsername_AsksForAnother()
    {
        var id = AddUnlinked("bob", "bob@example.com");
        _server.On("POST v1/auth/register", req =>
            req.Content!.ReadFromJsonAsync<RegisterRequest>(ApiFormat.Json).Result!.Username == "bob"
                ? FakeSocialServer.Respond(HttpStatusCode.Conflict, FakeSocialServer.Problem(409, "That username is taken.", "username"))
                : FakeSocialServer.Respond(HttpStatusCode.Created, new AuthResponse("t", new MeResponse(9, "bob_lifts", "bob@example.com", null))));

        var taken = await _accounts.MoveOnlineAsync(id, Pw);
        Assert.Equal(("That username is taken.", FormField.Username), (taken.Error, taken.Field));
        Assert.Null(Local(id).ServerUserId);

        var r = await _accounts.MoveOnlineAsync(id, Pw, username: "bob_lifts");
        Assert.True(r.Success, r.Error);
        Assert.Equal(("bob_lifts", 9), (Local(id).Username, Local(id).ServerUserId));
    }

    [Fact] public async Task MoveOnline_WrongPassword_OrALinkedAccount_IsRefused()
    {
        var unlinked = AddUnlinked("bob", "bob@example.com");
        var linked = AddLinked(Alice);

        Assert.Equal(AccountService.WrongLogin, (await _accounts.MoveOnlineAsync(unlinked, "wrong password")).Error);
        Assert.Equal(AccountService.WrongLogin, (await _accounts.MoveOnlineAsync(linked, Pw)).Error);
        Assert.Empty(_server.Calls);
    }

    // --- Restore ---

    private async Task<int> SignedInAsync()
    {
        _server.Accepts(Alice);
        var r = await _accounts.SignInAsync("alice", Pw);
        Assert.True(r.Success, r.Error);
        return r.User!.Id;
    }

    [Fact] public async Task Restore_Ok_SignsIn_AndRefreshesTheCache()
    {
        var id = await SignedInAsync();
        _session.SignOut();
        _server.On("GET v1/me", HttpStatusCode.OK, Alice with { Email = "new@example.com" });

        Assert.True(await _accounts.RestoreAsync());

        Assert.Equal(id, _session.CurrentUser!.Id);
        Assert.False(_session.SignedInOffline);
        Assert.Equal("new@example.com", Local(id).Email);
    }

    [Fact] public async Task Restore_401_ClearsTheToken()
    {
        await SignedInAsync();
        _session.SignOut();
        _server.On("GET v1/me", HttpStatusCode.Unauthorized);

        Assert.False(await _accounts.RestoreAsync());
        Assert.Null(_server.Tokens.Get());
        Assert.False(_session.IsLoggedIn);
    }

    [Fact] public async Task Restore_Offline_SignsInAsTheTokensOwner_FromTheCache()
    {
        AddLinked(new MeResponse(7, "carol", "carol@example.com", null));   // another linked account on this computer
        var id = await SignedInAsync();
        _session.SignOut();
        _server.Offline = true;

        Assert.True(await _accounts.RestoreAsync());
        Assert.Equal(id, _session.CurrentUser!.Id);
        Assert.True(_session.SignedInOffline);
        Assert.Equal("token-1", _server.Tokens.Get());
    }

    [Fact] public async Task Restore_WithNoToken_DoesNothing()
    {
        Assert.False(await _accounts.RestoreAsync());
        Assert.Empty(_server.Calls);
    }

    // --- Log out, session end ---

    [Fact] public async Task LogOut_ClearsTokenAndCache_ButKeepsWorkouts()
    {
        var id = await SignedInAsync();
        AddWorkout(id, new DateOnly(2026, 10, 18));
        using (var db = _db.CreateDbContext())
        {
            db.SocialCache.Add(new SocialCache { UserId = id, Key = SocialCacheKeys.Friends, Json = "[]", FetchedAt = DateTime.UtcNow });
            db.SaveChanges();
        }
        _server.On("POST v1/auth/logout", HttpStatusCode.NoContent);

        await _accounts.LogOutAsync();

        Assert.Contains("POST v1/auth/logout", _server.Calls);
        Assert.Null(_server.Tokens.Get());
        Assert.False(_session.IsLoggedIn);
        Assert.Null(_session.SignOutNotice);
        using var check = _db.CreateDbContext();
        Assert.Empty(check.SocialCache);
        Assert.Single(check.WorkoutLogs);
        Assert.Null(Local(id).RememberTokenHash);   // a restart offline can't sign back in
    }

    [Fact] public async Task LogOut_Offline_StillSignsOut()
    {
        await SignedInAsync();
        _server.Offline = true;

        await _accounts.LogOutAsync();

        Assert.Null(_server.Tokens.Get());
        Assert.False(_session.IsLoggedIn);
    }

    [Fact] public async Task SessionEnd_SignsOutWithTheNotice()
    {
        await SignedInAsync();

        await _accounts.EndSessionAsync();

        Assert.False(_session.IsLoggedIn);
        Assert.Equal(AccountService.SessionEndedNotice, _session.SignOutNotice);
        Assert.Null(_server.Tokens.Get());
    }

    // --- Account changes ---

    [Fact] public async Task ChangeEmail_UpdatesTheCache_AfterTheServer()
    {
        var id = await SignedInAsync();
        _server.On("PUT v1/me/email", HttpStatusCode.BadRequest, FakeSocialServer.Problem(400, "That password is not right.", "password"));
        var wrong = await _accounts.ChangeEmailAsync("new@example.com", "wrong password");
        Assert.Equal(FormField.Password, wrong.Field);
        Assert.Equal("alice@example.com", Local(id).Email);

        _server.On("PUT v1/me/email", HttpStatusCode.NoContent);
        var r = await _accounts.ChangeEmailAsync(" New@Example.com", Pw);

        Assert.True(r.Success, r.Error);
        Assert.Equal("new@example.com", Local(id).Email);
        Assert.Equal("new@example.com", _session.CurrentUser!.Email);
    }

    [Fact] public async Task ChangePassword_UpdatesTheCachedHash()
    {
        var id = await SignedInAsync();
        _server.On("PUT v1/me/password", HttpStatusCode.NoContent);

        var r = await _accounts.ChangePasswordAsync(Pw, "brand new pass");

        Assert.True(r.Success, r.Error);
        Assert.True(PasswordHasher.Verify("brand new pass", Local(id).PasswordHash));
    }

    [Fact] public async Task DeleteAccount_RemovesTheLocalUser_OnlyAfterTheServerSaid204()
    {
        var id = await SignedInAsync();
        var other = AddUnlinked("bob", null);
        AddWorkout(id, new DateOnly(2026, 10, 18));
        AddWorkout(other, new DateOnly(2026, 10, 18));

        _server.On("DELETE v1/me", HttpStatusCode.BadRequest, FakeSocialServer.Problem(400, "That password is not right.", "password"));
        Assert.Equal("That password is not right.", (await _accounts.DeleteAccountAsync("wrong password")).Error);
        _server.Offline = true;
        Assert.Contains("needs a connection", (await _accounts.DeleteAccountAsync(Pw)).Error);
        using (var db = _db.CreateDbContext()) Assert.Equal(2, db.Users.Count());
        Assert.True(_session.IsLoggedIn);

        _server.Offline = false;
        _server.On("DELETE v1/me", HttpStatusCode.NoContent);
        var r = await _accounts.DeleteAccountAsync(Pw);

        Assert.True(r.Success, r.Error);
        Assert.False(_session.IsLoggedIn);
        Assert.Null(_server.Tokens.Get());
        using var check = _db.CreateDbContext();
        Assert.Equal(other, check.Users.Single().Id);
        Assert.Equal(other, check.WorkoutLogs.Single().UserId);
    }

    [Fact] public async Task DeleteAccount_WithLogsAtTheirOwnCustomGym_DeletesEverything()
    {
        var id = await SignedInAsync();
        using (var db = _db.CreateDbContext())
        {
            var city = new City { Name = "Timișoara", OsmType = OsmElementType.Relation, OsmId = 7 };
            var gym = new Gym { Name = "Garage gym", City = city, OwnerUserId = id };
            db.Gyms.Add(gym);
            db.SaveChanges();
            db.WorkoutLogs.Add(new WorkoutLog { UserId = id, Date = new DateOnly(2026, 10, 18), GymId = gym.Id });
            db.SaveChanges();
        }
        _server.On("DELETE v1/me", HttpStatusCode.NoContent);

        Assert.True((await _accounts.DeleteAccountAsync(Pw)).Success);

        using var check = _db.CreateDbContext();
        Assert.Empty(check.Users);
        Assert.Empty(check.Gyms);
        Assert.Empty(check.WorkoutLogs);
    }
}
