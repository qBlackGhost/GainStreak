using System.Net;
using GymApp.App;
using GymApp.App.ViewModels;
using GymApp.Contracts;
using GymApp.Data.Models;
using GymApp.Services;
using GymApp.Social;
using Microsoft.Extensions.DependencyInjection;
using Relation = GymApp.Social.FriendRelation;

namespace GymApp.Tests;

/// <summary>Reading invite links (Phase 21).</summary>
public class InviteLinkTests
{
    [Theory]
    [InlineData("https://gainstreak.app/add/abcdefghjk", "abcdefghjk")]
    [InlineData("https://gainstreak.app/add/abcdefghjk/", "abcdefghjk")]
    [InlineData("https://gainstreak.app/add/ABCDEFGHJK?utm=qr", "abcdefghjk")]
    [InlineData("http://localhost:5080/add/abcdefghjk", "abcdefghjk")]
    [InlineData("https://gainstreak.app/ADD/abcdefghjk", "abcdefghjk")]
    [InlineData("https://gainstreak.app/add/", null)]
    [InlineData("https://gainstreak.app/add/abc", null)]
    [InlineData("https://gainstreak.app/add/abcdefghj0", null)]
    [InlineData("https://gainstreak.app/add/abcdefghjk/more", null)]
    [InlineData("https://gainstreak.app/privacy.html", null)]
    [InlineData("gainstreak://add/abcdefghjk", null)]
    public void TryParse(string link, string? code) => Assert.Equal(code, InviteLink.TryParse(new Uri(link)));

    [Fact] public void TryParse_Null_IsNull() => Assert.Null(InviteLink.TryParse(null));

    [Fact] public void For_UsesTheServersLink_ElseTheDefaultHost()
    {
        Assert.Equal("https://x.test/add/abcdefghjk", InviteLink.For(new InviteCodeResult(true, "abcdefghjk", Url: "https://x.test/add/abcdefghjk")));
        Assert.Equal("https://gainstreak.app/add/abcdefghjk", InviteLink.For(new InviteCodeResult(true, "abcdefghjk")));
    }
}

/// <summary><see cref="RemoteFriendService"/>'s invite calls against a fake server.</summary>
public sealed class RemoteInviteTests : IDisposable
{
    private const string Code = "abcdefghjk";
    private readonly TestDb _db = new();
    private readonly FakeSocialServer _server = new();
    private readonly SessionService _session = new();
    private readonly RemoteFriendService _friends;
    private readonly int _me;

    public RemoteInviteTests()
    {
        _me = _db.AddUser("alice");
        using (var db = _db.CreateDbContext())
            _session.SignIn(db.Users.Single(u => u.Id == _me));
        _friends = new RemoteFriendService(_server.Api, new SocialCacheStore(_db), _session);
    }

    public void Dispose() => _db.Dispose();

    [Fact] public async Task OwnInvite_ComesFromTheServer_AndIsSavedForOffline()
    {
        _server.On("GET v1/friends/invite", HttpStatusCode.OK, new FriendInviteResponse(Code, $"https://gainstreak.app/add/{Code}"));

        var online = await _friends.GetInviteCodeAsync(_me);
        Assert.Equal(new InviteCodeResult(true, Code, Url: $"https://gainstreak.app/add/{Code}"), online);

        _server.Offline = true;
        var offline = await _friends.GetInviteCodeAsync(_me);
        Assert.Equal(online with { IsStale = true }, offline);
    }

    [Fact] public async Task OwnInvite_OfflineWithNothingSaved_SaysOffline()
    {
        _server.Offline = true;
        var r = await _friends.GetInviteCodeAsync(_me);
        Assert.False(r.Success);
        Assert.NotNull(r.Error);
    }

    [Fact] public async Task Reset_SavesTheNewLink_AndOfflineChangesNothing()
    {
        _server.On("GET v1/friends/invite", HttpStatusCode.OK, new FriendInviteResponse(Code, "https://gainstreak.app/add/" + Code));
        _server.On("POST v1/friends/invite/reset", HttpStatusCode.OK, new FriendInviteResponse("zyxwvutsrq", "https://gainstreak.app/add/zyxwvutsrq"));
        await _friends.GetInviteCodeAsync(_me);

        Assert.Equal("zyxwvutsrq", (await _friends.ResetInviteCodeAsync(_me)).Code);
        _server.Offline = true;
        Assert.False((await _friends.ResetInviteCodeAsync(_me)).Success);
        Assert.Equal("zyxwvutsrq", (await _friends.GetInviteCodeAsync(_me)).Code);
    }

    [Fact] public async Task LookUp_MapsTheInviterAndRelation()
    {
        _server.On($"GET v1/invites/{Code}", HttpStatusCode.OK, new InviteLookupResponse(901, "bob", GymApp.Contracts.FriendRelation.RequestReceived));
        var r = await _friends.LookUpInviteAsync(_me, " ABCDEFGHJK ");
        Assert.Equal(new InviteLookup(true, new SocialUser(901, "bob"), Relation.RequestReceived), r);
    }

    [Fact] public async Task LookUp_UnknownCode_CarriesTheServersMessage()
    {
        _server.On($"GET v1/invites/{Code}", HttpStatusCode.NotFound, FakeSocialServer.Problem(404, FriendService.UnknownInviteError));
        Assert.Equal(FriendService.UnknownInviteError, (await _friends.LookUpInviteAsync(_me, Code)).Error);
    }

    [Fact] public async Task MalformedCodes_SendNothing()
    {
        Assert.Equal(FriendService.UnknownInviteError, (await _friends.LookUpInviteAsync(_me, "nope")).Error);
        Assert.Equal(FriendService.UnknownInviteError, (await _friends.AcceptInviteAsync(_me, "nope")).Error);
        Assert.Empty(_server.Calls);
    }

    [Fact] public async Task Accept_BecomesFriends_And409IsTheConflictMessage()
    {
        _server.On($"POST v1/invites/{Code}/accept", HttpStatusCode.OK, new SendRequestResponse(true));
        Assert.Equal(new FriendResult(true, BecameFriends: true), await _friends.AcceptInviteAsync(_me, Code));

        _server.On($"POST v1/invites/{Code}/accept", HttpStatusCode.Conflict, FakeSocialServer.Problem(409, "anything"));
        Assert.Equal(FriendService.ConflictError, (await _friends.AcceptInviteAsync(_me, Code)).Error);
    }

    [Fact] public async Task Offline_LookUpAndAccept_SayOffline()
    {
        _server.Offline = true;
        Assert.False((await _friends.LookUpInviteAsync(_me, Code)).Success);
        Assert.False((await _friends.AcceptInviteAsync(_me, Code)).Success);
    }

    [Fact] public async Task OnlyTheSignedInUser_CanBeAskedFor() =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => _friends.GetInviteCodeAsync(_me + 1));
}

/// <summary>An invite link opening Friends and its confirm card (<see cref="MainViewModel.OpenLink"/>,
/// <see cref="FriendsViewModel"/>), in the TestBench's local mode: bob's code, alice scans it.</summary>
[Collection(DataDirectoryCollection.Name)]
public sealed class OpenInviteLinkTests : IAsyncLifetime
{
    private readonly TempDataDirectory _data = new();
    private ServiceProvider _sp = null!;
    private int _bob;
    private string _bobsCode = "";

    private Uri Link => new($"https://gainstreak.app/add/{_bobsCode}");

    public async Task InitializeAsync()
    {
        _sp = AppServices.Create(localSocial: true, new FakePlatform()).BuildServiceProvider();
        Assert.Null(await AppStartup.PrepareAsync(_sp, localSocial: true));
        var accounts = _sp.GetRequiredService<IAccountService>();
        Assert.True((await accounts.RegisterAsync("bob", "bob@example.com", "correct horse 1")).Success);
        _bob = _sp.GetRequiredService<SessionService>().CurrentUser!.Id;
        _bobsCode = (await _sp.GetRequiredService<IFriendService>().GetInviteCodeAsync(_bob)).Code!;
        await accounts.LogOutAsync();
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        _data.Dispose();
    }

    private async Task<MainViewModel> AliceSignedInAsync()
    {
        Assert.True((await _sp.GetRequiredService<IAccountService>().RegisterAsync("alice", "alice@example.com", "correct horse 1")).Success);
        var shell = _sp.GetRequiredService<MainViewModel>();
        shell.ShowHome();
        return shell;
    }

    private static async Task Until(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++) await Task.Delay(10);
        Assert.True(done());
    }

    private static async Task<FriendsViewModel> CardAsync(MainViewModel shell)
    {
        var friends = Assert.IsType<FriendsViewModel>(shell.Current);
        await Until(() => !friends.IsInviteChecking);
        return friends;
    }

    [Fact] public async Task SignedIn_OpensFriends_AndAsksAboutTheInviter()
    {
        var shell = await AliceSignedInAsync();

        Assert.True(shell.OpenLink(Link));
        var friends = await CardAsync(shell);

        Assert.False(shell.HasPendingInvite);
        Assert.True(friends.HasInvite);
        Assert.True(friends.IsInviteAsk);
        Assert.Equal(("bob", "BO"), (friends.InviterName, friends.InviterInitials));
    }

    [Fact] public async Task Accept_MakesFriends_AndSaysSo()
    {
        var shell = await AliceSignedInAsync();
        shell.OpenLink(Link);
        var friends = await CardAsync(shell);

        await friends.AcceptInviteCommand.ExecuteAsync(null);

        Assert.True(friends.IsInviteMessage);
        Assert.True(friends.IsInviteGood);
        Assert.Equal("You and bob are now friends.", friends.InviteMessage);
        Assert.Equal("Done", friends.InviteOkLabel);
        Assert.Equal(["bob"], friends.Friends.Select(f => f.Username));

        friends.DismissInviteCommand.Execute(null);
        Assert.False(friends.HasInvite);
    }

    [Fact] public async Task NotNow_HidesTheCard_AndGoingBackDoesntBringItBack()
    {
        var shell = await AliceSignedInAsync();
        shell.OpenLink(Link);
        var friends = await CardAsync(shell);

        friends.DismissInviteCommand.Execute(null);
        Assert.False(friends.HasInvite);

        shell.ShowFriends();
        Assert.False(Assert.IsType<FriendsViewModel>(shell.Current).HasInvite);
    }

    [Fact] public async Task AlreadyFriends_SaysSo()
    {
        var shell = await AliceSignedInAsync();
        shell.OpenLink(Link);
        await (await CardAsync(shell)).AcceptInviteCommand.ExecuteAsync(null);

        shell.OpenLink(Link);
        var again = await CardAsync(shell);
        Assert.True(again.IsInviteMessage);
        Assert.Equal("You're already friends with bob.", again.InviteMessage);
    }

    [Fact] public async Task AResetCode_DoesntWorkAnyMore()
    {
        var old = Link;
        await _sp.GetRequiredService<IFriendService>().ResetInviteCodeAsync(_bob);
        var shell = await AliceSignedInAsync();

        shell.OpenLink(old);
        var friends = await CardAsync(shell);

        Assert.Equal(FriendService.UnknownInviteError, friends.InviteMessage);
        Assert.Equal("IcLinkOff", friends.InviteIcon);
        Assert.False(friends.CanRetryInvite);
    }

    [Fact] public async Task OwnCode_IsRefused()
    {
        var shell = await AliceSignedInAsync();
        var me = _sp.GetRequiredService<SessionService>().CurrentUser!.Id;
        var mine = (await _sp.GetRequiredService<IFriendService>().GetInviteCodeAsync(me)).Code;

        shell.OpenLink(new Uri($"https://gainstreak.app/add/{mine}"));

        Assert.Equal(FriendService.OwnInviteError, (await CardAsync(shell)).InviteMessage);
    }

    [Fact] public async Task OtherLinks_AreIgnored()
    {
        var shell = await AliceSignedInAsync();
        Assert.False(shell.OpenLink(new Uri("https://gainstreak.app/privacy.html")));
        Assert.IsType<HomeViewModel>(shell.Current);
        Assert.False(shell.HasPendingInvite);
    }

    [Fact] public async Task BeforeSignIn_TheCodeWaits_AndTheCardShowsOnceSignedIn()
    {
        var shell = _sp.GetRequiredService<MainViewModel>();
        Assert.IsType<LoginViewModel>(shell.Current);

        Assert.True(shell.OpenLink(Link));
        Assert.IsType<LoginViewModel>(shell.Current);
        Assert.True(shell.HasPendingInvite);

        Assert.True((await _sp.GetRequiredService<IAccountService>().RegisterAsync("alice", "alice@example.com", "correct horse 1")).Success);
        shell.ShowHome();   // what profile setup's Skip does

        Assert.True((await CardAsync(shell)).IsInviteAsk);

        // Only once: the next screen is the one asked for
        shell.ShowHome();
        Assert.IsType<HomeViewModel>(shell.Current);
    }
}

/// <summary>"Your QR code" (<see cref="FriendQrViewModel"/>).</summary>
public sealed class FriendQrViewModelTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly FakeSocialServer _server = new();
    private readonly SessionService _session = new();
    private readonly FakePlatform _platform = new();
    private readonly FriendQrViewModel _vm;

    public FriendQrViewModelTests()
    {
        var me = _db.AddUser("alice");
        using (var db = _db.CreateDbContext())
            _session.SignIn(db.Users.Single(u => u.Id == me));
        _vm = new FriendQrViewModel(new RemoteFriendService(_server.Api, new SocialCacheStore(_db), _session), _session, _platform);
        _server.On("GET v1/friends/invite", HttpStatusCode.OK, new FriendInviteResponse("k7m2x9pqrt", "https://gainstreak.app/add/k7m2x9pqrt"));
        _server.On("POST v1/friends/invite/reset", HttpStatusCode.OK, new FriendInviteResponse("w3n8c5hzvd", "https://gainstreak.app/add/w3n8c5hzvd"));
    }

    public void Dispose() => _db.Dispose();

    [Fact] public async Task Open_ShowsTheLink()
    {
        await _vm.OpenAsync();

        Assert.True(_vm.IsOpen);
        Assert.Equal("https://gainstreak.app/add/k7m2x9pqrt", _vm.Link);
        Assert.Equal("gainstreak.app/add/k7m2x9pqrt", _vm.LinkText);
        Assert.Equal("@alice", _vm.Username);
        Assert.True(_vm.CanReset);
        Assert.Null(_vm.Notice);
    }

    [Fact] public async Task Share_OnlyWhereThePlatformCan()
    {
        await _vm.OpenAsync();
        Assert.False(_vm.CanShare);
        Assert.False(_vm.ShareCommand.CanExecute(null));

        _platform.CanShare = true;
        var phone = new FriendQrViewModel(new RemoteFriendService(_server.Api, new SocialCacheStore(_db), _session), _session, _platform);
        await phone.OpenAsync();
        phone.ShareCommand.Execute(null);
        Assert.Equal(["https://gainstreak.app/add/k7m2x9pqrt"], _platform.Shared);
    }

    [Fact] public async Task Copied_ShowsLinkCopied_ForAMoment()
    {
        FriendQrViewModel.CopiedFor = TimeSpan.FromMilliseconds(50);
        await _vm.OpenAsync();

        _vm.Copied(true);
        Assert.True(_vm.IsCopied);
        for (var i = 0; i < 100 && _vm.IsCopied; i++) await Task.Delay(10);
        Assert.False(_vm.IsCopied);

        _vm.Copied(false);
        Assert.False(_vm.IsCopied);
        Assert.Equal("Could not copy the link.", _vm.Error);
    }

    [Fact] public async Task Reset_AsksFirst_ThenShowsTheNewLink()
    {
        await _vm.OpenAsync();

        _vm.AskResetCommand.Execute(null);
        Assert.True(_vm.IsConfirmingReset);
        Assert.False(_vm.CanReset);
        _vm.CancelResetCommand.Execute(null);
        Assert.Equal("https://gainstreak.app/add/k7m2x9pqrt", _vm.Link);

        _vm.AskResetCommand.Execute(null);
        await _vm.ConfirmResetCommand.ExecuteAsync(null);
        Assert.False(_vm.IsConfirmingReset);
        Assert.Equal("https://gainstreak.app/add/w3n8c5hzvd", _vm.Link);
        Assert.Equal(FriendQrViewModel.JustResetText, _vm.Notice);
    }

    [Fact] public async Task Offline_ShowsTheSavedLink_AndNoReset()
    {
        await _vm.OpenAsync();
        _vm.CloseCommand.Execute(null);
        _server.Offline = true;

        await _vm.OpenAsync();

        Assert.Equal("https://gainstreak.app/add/k7m2x9pqrt", _vm.Link);
        Assert.True(_vm.IsOfflineSaved);
        Assert.Equal(FriendQrViewModel.OfflineSavedText, _vm.Notice);
        Assert.False(_vm.CanReset);
    }

    [Fact] public async Task Offline_WithNothingSaved_SaysSo()
    {
        _server.Offline = true;
        await _vm.OpenAsync();

        Assert.False(_vm.HasLink);
        Assert.True(_vm.HasNoLink);
        Assert.Equal("Connect to the internet to get your link.", _vm.NoLinkText);
        Assert.False(_vm.AskResetCommand.CanExecute(null));
    }
}
