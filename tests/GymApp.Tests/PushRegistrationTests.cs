using System.Net;
using GymApp.App;
using GymApp.App.ViewModels;
using GymApp.Contracts;
using GymApp.Services;
using GymApp.Social;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GymApp.Tests;

/// <summary>A phone's push side for tests: a permission, what asking answers, and a token.</summary>
public sealed class FakePush : IPushNotifications
{
    public bool IsSupported { get; set; } = true;
    public PushPermission Permission { get; set; } = PushPermission.NotAsked;
    /// <summary>What the system prompt answers.</summary>
    public bool Allows { get; set; } = true;
    public int Asked { get; private set; }
    public PushToken? Token { get; set; } = new(PushPlatform.Apns, "aaaa1111", true);

    public Task<PushPermission> GetPermissionAsync() => Task.FromResult(Permission);

    public Task<bool> RequestPermissionAsync()
    {
        Asked++;
        if (Permission == PushPermission.NotAsked) Permission = Allows ? PushPermission.Allowed : PushPermission.Denied;
        return Task.FromResult(Permission == PushPermission.Allowed);
    }

    public Task<PushToken?> GetTokenAsync() => Task.FromResult(Token);

    public event Action? TokenChanged;
    public void ChangeToken(string token)
    {
        Token = Token! with { Token = token };
        TokenChanged?.Invoke();
    }
}

/// <summary>Friend notifications' switch and the server's copy of the token (Phase 22c), against a fake server.</summary>
public sealed class PushRegistrationTests : IDisposable
{
    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset? Fixed { get; set; }
        public override DateTimeOffset GetUtcNow() => Fixed ?? base.GetUtcNow();
    }

    private const string Put = "PUT v1/me/push-device", Delete = "DELETE v1/me/push-device";

    private readonly TestDb _db = new();
    private readonly FakeSocialServer _server = new();
    private readonly SessionService _session = new();
    private readonly FakePush _push = new();
    private readonly TestClock _clock = new() { Fixed = new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero) };
    private readonly RemotePushRegistration _registration;
    private readonly int _me;

    public PushRegistrationTests()
    {
        _me = _db.AddUser("alice");
        using (var db = _db.CreateDbContext())
        {
            var user = db.Users.Single(u => u.Id == _me);
            user.ServerUserId = 501;
            db.SaveChanges();
            _session.SignIn(user);
        }
        _server.Tokens.Set("session-1");
        _server.On(Put, HttpStatusCode.NoContent);
        _server.On(Delete, HttpStatusCode.NoContent);
        _registration = new RemotePushRegistration(_server.Api, _server.Tokens, _push, _db, _session, clock: _clock);
    }

    public void Dispose() => _db.Dispose();

    private int Calls(string route) => _server.Calls.Count(c => c == route);

    private bool? Saved()
    {
        using var db = _db.CreateDbContext();
        return db.Users.AsNoTracking().Single(u => u.Id == _me).FriendNotifications;
    }

    [Fact] public async Task TurningOn_AsksOnce_ThenRegistersTheToken()
    {
        Assert.Equal(new PushState(true, null, false, PushPermission.NotAsked), await _registration.GetStateAsync());

        Assert.Equal(PushSwitchResult.On, await _registration.SetEnabledAsync(true));

        Assert.Equal(1, _push.Asked);
        Assert.True(Saved());
        Assert.Equal(new PushDeviceRequest(PushPlatform.Apns, "aaaa1111", true), _server.LastBody<PushDeviceRequest>(Put));
        Assert.Equal(new PushState(true, true, false, PushPermission.Allowed), await _registration.GetStateAsync());
    }

    [Fact] public async Task Refused_LeavesItOff_AndSendsNothing()
    {
        _push.Allows = false;
        Assert.Equal(PushSwitchResult.Refused, await _registration.SetEnabledAsync(true));
        Assert.False(Saved());
        Assert.Empty(_server.Calls);

        // Already refused: turning on doesn't show a prompt the phone won't show again
        Assert.Equal(PushSwitchResult.Refused, await _registration.SetEnabledAsync(true));
        Assert.Equal(PushPermission.Denied, (await _registration.GetStateAsync()).Permission);
    }

    [Fact] public async Task AlreadyAllowed_TurnsOnByItself_WithoutAsking()
    {
        _push.Permission = PushPermission.Allowed;   // Reminders asked before
        await _registration.SyncAsync();
        Assert.True(Saved());
        Assert.Equal(0, _push.Asked);
        Assert.Equal(1, Calls(Put));
    }

    [Fact] public async Task NotAllowedYet_StaysUndecided_AndSendsNothing()
    {
        await _registration.SyncAsync();
        Assert.Null(Saved());
        Assert.Empty(_server.Calls);
    }

    [Fact] public async Task AnUnchangedToken_IsSentOnceADay_AndAChangedOneAtOnce()
    {
        await _registration.SetEnabledAsync(true);
        await _registration.SyncAsync();
        _clock.Fixed += RemotePushRegistration.ResendAfter - TimeSpan.FromMinutes(1);
        await _registration.SyncAsync();
        Assert.Equal(1, Calls(Put));

        _clock.Fixed += TimeSpan.FromMinutes(1);
        await _registration.SyncAsync();
        Assert.Equal(2, Calls(Put));

        _push.Token = _push.Token! with { Token = "bbbb2222" };
        await _registration.SyncAsync();
        Assert.Equal(3, Calls(Put));
        Assert.Equal("bbbb2222", _server.LastBody<PushDeviceRequest>(Put)!.Token);
    }

    [Fact] public async Task ANewSession_RegistersAgain()
    {
        await _registration.SetEnabledAsync(true);
        _server.Tokens.Set("session-2");   // signed out and in: the server has no device for this session
        await _registration.SyncAsync();
        Assert.Equal(2, Calls(Put));
    }

    [Fact] public async Task TurningOff_RemovesIt_Once()
    {
        await _registration.SetEnabledAsync(true);
        Assert.Equal(PushSwitchResult.Off, await _registration.SetEnabledAsync(false));
        await _registration.SyncAsync();
        Assert.False(Saved());
        Assert.Equal(1, Calls(Delete));

        Assert.Equal(PushSwitchResult.On, await _registration.SetEnabledAsync(true));
        Assert.Equal(2, Calls(Put));
    }

    [Fact] public async Task TurningOff_OnANewSession_HasNothingToRemove()
    {
        await _registration.SetEnabledAsync(true);
        _server.Tokens.Set("session-2");
        await _registration.SetEnabledAsync(false);
        Assert.Equal(0, Calls(Delete));
    }

    [Fact] public async Task PermissionTakenAwayInSettings_RemovesIt()
    {
        await _registration.SetEnabledAsync(true);
        _push.Permission = PushPermission.Denied;
        await _registration.SyncAsync();
        Assert.Equal(1, Calls(Delete));
        Assert.True(Saved());   // the switch stays on: allowing again brings them back
    }

    [Fact] public async Task Offline_OrAnOldServer_TriesAgainNextTime()
    {
        _server.Offline = true;
        await _registration.SetEnabledAsync(true);
        Assert.True(Saved());

        _server.Offline = false;
        _server.On(Put, HttpStatusCode.NotFound);   // a server before 0.22.0
        await _registration.SyncAsync();
        _server.On(Put, HttpStatusCode.NoContent);
        await _registration.SyncAsync();
        await _registration.SyncAsync();
        Assert.Equal(2, Calls(Put));
    }

    [Fact] public async Task OfflineTurningOff_RemovesItWhenBackOnline()
    {
        await _registration.SetEnabledAsync(true);
        _server.Offline = true;
        await _registration.SetEnabledAsync(false);
        _server.Offline = false;
        await _registration.SyncAsync();
        Assert.Equal(1, Calls(Delete));
    }

    [Fact] public async Task NoTokenYet_WaitsForIt()
    {
        _push.Token = null;
        await _registration.SetEnabledAsync(true);
        Assert.Equal(0, Calls(Put));

        _push.Token = new PushToken(PushPlatform.Fcm, "fcm-token", false);
        await _registration.SyncAsync();
        Assert.Equal(new PushDeviceRequest(PushPlatform.Fcm, "fcm-token", false), _server.LastBody<PushDeviceRequest>(Put));
    }

    [Fact] public async Task Unsupported_OrNotLinked_OrSignedOut_DoNothing()
    {
        _push.IsSupported = false;
        Assert.False((await _registration.GetStateAsync()).IsSupported);
        Assert.Equal(PushSwitchResult.Off, await _registration.SetEnabledAsync(true));
        Assert.Null(Saved());

        _push.IsSupported = true;
        _session.CurrentUser!.ServerUserId = null;   // an account never moved online
        await _registration.SetEnabledAsync(true);
        Assert.True(Saved());
        Assert.Empty(_server.Calls);

        _session.SignOut();
        await _registration.SyncAsync();
        Assert.Equal(PushSwitchResult.Off, await _registration.SetEnabledAsync(true));
        Assert.Empty(_server.Calls);
    }

    [Fact] public async Task TheCard_StaysDismissed()
    {
        await _registration.DismissCardAsync();
        Assert.True((await _registration.GetStateAsync()).CardDismissed);
        Assert.True(_session.CurrentUser!.FriendNotificationsCardDismissed);
    }

    [Fact] public async Task RequestSync_RunsInTheBackground()
    {
        _push.Permission = PushPermission.Allowed;
        _registration.RequestSync();
        await _registration.WhenIdleAsync();
        Assert.Equal(1, Calls(Put));
    }

    [Fact] public async Task LocalMode_KeepsTheSwitch_AndSendsNothing()
    {
        var local = new LocalPushRegistration(_push, _db, _session);
        Assert.Equal(PushSwitchResult.On, await local.SetEnabledAsync(true));
        Assert.True(Saved());
        await local.SyncAsync();
        Assert.Empty(_server.Calls);
    }
}

/// <summary>A tapped or arriving friend notification in the shell (Phase 22c), in the TestBench's local mode.</summary>
[Collection(DataDirectoryCollection.Name)]
public sealed class PushRoutingTests : IAsyncLifetime
{
    private readonly TempDataDirectory _data = new();
    private ServiceProvider _sp = null!;

    public async Task InitializeAsync()
    {
        _sp = AppServices.Create(localSocial: true, new FakePlatform()).BuildServiceProvider();
        Assert.Null(await AppStartup.PrepareAsync(_sp, localSocial: true));
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        _data.Dispose();
    }

    private async Task<MainViewModel> SignedInAsync()
    {
        Assert.True((await _sp.GetRequiredService<IAccountService>().RegisterAsync("alice", "alice@example.com", "correct horse 1")).Success);
        var shell = _sp.GetRequiredService<MainViewModel>();
        shell.ShowHome();
        return shell;
    }

    [Fact] public async Task ARequest_OpensTheInboxsRequests_AndAnAcceptOpensFriends()
    {
        var shell = await SignedInAsync();
        shell.OpenFromPush(PushKind.FriendRequest);
        Assert.Equal(InboxViewModel.RequestsFilter, Assert.IsType<InboxViewModel>(shell.Current).FilterIndex);

        shell.OpenFromPush(PushKind.FriendAccepted);
        Assert.IsType<FriendsViewModel>(shell.Current);
        shell.ShowHome();
        shell.OpenFromPush(PushKind.InviteAccepted);
        Assert.IsType<FriendsViewModel>(shell.Current);
    }

    [Fact] public void SignedOut_ATapIsDropped()
    {
        var shell = _sp.GetRequiredService<MainViewModel>();
        Assert.IsType<LoginViewModel>(shell.Current);
        shell.OpenFromPush(PushKind.FriendRequest);
        Assert.IsType<LoginViewModel>(shell.Current);
    }

    [Fact] public async Task Arriving_OnInboxOrFriends_ReloadsInsteadOfShowing()
    {
        var shell = await SignedInAsync();
        Assert.True(shell.PushArrived(PushKind.FriendRequest));   // Home: show it

        shell.ShowInbox();
        Assert.False(shell.PushArrived(PushKind.FriendRequest));
        shell.ShowFriends();
        Assert.False(shell.PushArrived(PushKind.FriendAccepted));
    }

    [Fact] public void PayloadKinds_ReadBackFromTheirNames()
    {
        foreach (var kind in Enum.GetValues<PushKind>()) Assert.Equal(kind, PushPayload.Parse(PushPayload.Name(kind)));
        Assert.Null(PushPayload.Parse("shareReceived"));
        Assert.Null(PushPayload.Parse(null));
    }

    [Fact] public void TheDesktopAndTheTests_HaveNoPush()
    {
        Assert.False(_sp.GetRequiredService<IPushNotifications>().IsSupported);
        Assert.IsType<LocalPushRegistration>(_sp.GetRequiredService<IPushRegistration>());
    }
}

/// <summary>Profile's Friend requests switch and Friends' card (Phase 22d), in the TestBench's local mode with a phone
/// that can receive pushes.</summary>
[Collection(DataDirectoryCollection.Name)]
public sealed class PushUiTests : IAsyncLifetime
{
    private readonly TempDataDirectory _data = new();
    private readonly FakePush _push = new();
    private ServiceProvider _sp = null!;
    private int _bob;

    public async Task InitializeAsync()
    {
        _sp = AppServices.Create(localSocial: true, new FakePlatform { Push = _push }).BuildServiceProvider();
        Assert.Null(await AppStartup.PrepareAsync(_sp, localSocial: true));
        var accounts = _sp.GetRequiredService<IAccountService>();
        Assert.True((await accounts.RegisterAsync("bob", "bob@example.com", "correct horse 1")).Success);
        _bob = _sp.GetRequiredService<SessionService>().CurrentUser!.Id;
        await accounts.LogOutAsync();
        Assert.True((await accounts.RegisterAsync("alice", "alice@example.com", "correct horse 1")).Success);
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        _data.Dispose();
    }

    private static async Task Until(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++) await Task.Delay(10);
        Assert.True(done());
    }

    private async Task<FriendsViewModel> FriendsWithARequestAsync()
    {
        Assert.True((await _sp.GetRequiredService<IFriendService>().SendRequestAsync(
            _sp.GetRequiredService<SessionService>().CurrentUser!.Id, _bob)).Success);
        return _sp.GetRequiredService<FriendsViewModel>();
    }

    // --- Profile ---

    [Fact] public async Task Switch_AsksThenStaysOn_AndLoadsAsSaved()
    {
        var profile = _sp.GetRequiredService<ProfileViewModel>();
        Assert.True(profile.HasNotifications);
        Assert.True(profile.FriendNotifications.IsSupported);

        profile.FriendNotifications.IsOn = true;
        await Until(() => _push.Asked == 1);
        await Until(() => (_sp.GetRequiredService<IPushRegistration>().GetStateAsync().Result).Enabled == true);

        var again = _sp.GetRequiredService<ProfileViewModel>();
        await Until(() => again.FriendNotifications.IsOn);
        Assert.Null(again.FriendNotifications.Note);
    }

    [Fact] public async Task Switch_Refused_GoesBackOff_WithTheNote()
    {
        _push.Allows = false;
        var profile = _sp.GetRequiredService<ProfileViewModel>();
        profile.FriendNotifications.IsOn = true;
        await Until(() => profile.FriendNotifications.Note is not null);
        Assert.False(profile.FriendNotifications.IsOn);
        Assert.Equal(FriendNotificationsViewModel.RefusedText, profile.FriendNotifications.Note);
    }

    [Fact] public async Task Switch_OnButTakenAwayInSettings_ShowsOffWithTheNote()
    {
        await _sp.GetRequiredService<IPushRegistration>().SetEnabledAsync(true);
        _push.Permission = PushPermission.Denied;
        var profile = _sp.GetRequiredService<ProfileViewModel>();
        await Until(() => profile.FriendNotifications.Note is not null);
        Assert.False(profile.FriendNotifications.IsOn);
    }

    // --- Friends' card ---

    [Fact] public async Task NoRequestsOrFriends_NoCard_UntilTheQrSheetOpens()
    {
        var friends = _sp.GetRequiredService<FriendsViewModel>();
        await Task.Delay(100);
        Assert.False(friends.Push.IsVisible);

        await friends.ShowQrCommand.ExecuteAsync(null);
        await Until(() => friends.Push.IsVisible);
        Assert.True(friends.Push.IsAsk);
    }

    [Fact] public async Task TurnOn_SaysSo_AndTheCardDoesntComeBack()
    {
        var friends = await FriendsWithARequestAsync();
        await Until(() => friends.Push.IsAsk);

        await friends.Push.TurnOnCommand.ExecuteAsync(null);
        Assert.True(friends.Push.IsMessage);
        Assert.True(friends.Push.IsGood);
        Assert.Equal((FriendPushCardViewModel.TurnedOnText, "Done", "IcCheck"), (friends.Push.Message, friends.Push.OkLabel, friends.Push.Icon));
        friends.Push.CloseCommand.Execute(null);
        Assert.False(friends.Push.IsVisible);

        var later = _sp.GetRequiredService<FriendsViewModel>();
        await Task.Delay(100);
        Assert.False(later.Push.IsVisible);
    }

    [Fact] public async Task Refused_PointsToSettings()
    {
        _push.Allows = false;
        var friends = await FriendsWithARequestAsync();
        await Until(() => friends.Push.IsAsk);
        await friends.Push.TurnOnCommand.ExecuteAsync(null);
        Assert.False(friends.Push.IsGood);
        Assert.Equal((FriendPushCardViewModel.RefusedText, "OK", "IcBellOff"), (friends.Push.Message, friends.Push.OkLabel, friends.Push.Icon));
    }

    [Fact] public async Task NotNow_IsForGood()
    {
        var friends = await FriendsWithARequestAsync();
        await Until(() => friends.Push.IsAsk);
        await friends.Push.NotNowCommand.ExecuteAsync(null);
        Assert.False(friends.Push.IsVisible);

        var later = _sp.GetRequiredService<FriendsViewModel>();
        await later.ShowQrCommand.ExecuteAsync(null);
        await Task.Delay(100);
        Assert.False(later.Push.IsVisible);
    }

    [Fact] public async Task AlreadyAllowed_NoCard()
    {
        _push.Permission = PushPermission.Allowed;   // Reminders asked: on by themselves
        var friends = await FriendsWithARequestAsync();
        await Task.Delay(100);
        Assert.False(friends.Push.IsVisible);
    }

    [Fact] public async Task WithoutPush_NeitherShows()
    {
        _push.IsSupported = false;
        var profile = _sp.GetRequiredService<ProfileViewModel>();
        Assert.False(profile.FriendNotifications.IsSupported);
        var friends = await FriendsWithARequestAsync();
        await Task.Delay(100);
        Assert.False(friends.Push.IsVisible);
    }
}
