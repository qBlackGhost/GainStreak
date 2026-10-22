using GymApp.Desktop.ViewModels;
using GymApp.Services;
using GymApp.Social;

namespace GymApp.Tests;

public class InboxTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gymapp-inbox-" + Guid.NewGuid().ToString("N"));
    private readonly SessionService _session = new();
    private readonly FriendService _friends;
    private readonly InboxService _inbox;
    private readonly int _alice, _bob, _cara;

    public InboxTests()
    {
        _alice = _db.AddUser("alice");
        _bob = _db.AddUser("bob");
        _cara = _db.AddUser("cara");
        using (var db = _db.CreateDbContext()) _session.SignIn(db.Users.Single(u => u.Id == _alice));
        _friends = new FriendService(new LocalSocialStore(_db));
        _inbox = new InboxService(() => [new FriendRequestInboxSource(_friends)], new InboxStore(_dir), _session);
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private InboxViewModel NewViewModel() => new(_inbox, _friends, _session, () => null!, new LocalShareService(), null!, null!);

    [Fact] public async Task IncomingRequests_AreNewUntilMarkedRead_AndTheBadgeFollows()
    {
        await _friends.SendRequestAsync(_bob, _alice);
        await _friends.SendRequestAsync(_alice, _cara);   // sent by alice: stays on Friends, not in her inbox
        var badge = 0;
        _inbox.UnreadChanged += () => badge = _inbox.UnreadCount;

        var view = await _inbox.GetAsync(_alice);

        var item = Assert.IsType<FriendRequestItem>(Assert.Single(view.Items));
        Assert.Equal(("bob", true), (item.FromUsername, item.IsNew));
        Assert.Equal(1, badge);

        _inbox.MarkAllRead(_alice, view);
        Assert.Equal(0, badge);
        Assert.False((await _inbox.GetAsync(_alice)).Items.Single().IsNew);   // remembered across loads
    }

    [Fact] public async Task ViewModel_Accept_MakesFriends_AndLeavesAnEarlierLine()
    {
        await _friends.SendRequestAsync(_bob, _alice);
        var vm = NewViewModel();
        await vm.LoadAsync();
        Assert.Equal("1 thing waiting · 1 new", vm.Subtitle);
        Assert.Equal("Requests 1", vm.RequestsLabel);

        await ((FriendRequestRowViewModel)vm.Rows.Single()).AcceptCommand.ExecuteAsync(null);

        Assert.Equal("bob", Assert.Single(await _friends.GetFriendsAsync(_alice)).Username);
        Assert.Empty(vm.Rows);
        Assert.True(vm.IsCaughtUp);
        Assert.Equal("You're now friends with bob", Assert.Single(vm.History).Text);
        Assert.Equal("Nothing waiting", vm.Subtitle);
        Assert.Equal(0, _inbox.UnreadCount);
    }

    [Fact] public async Task ViewModel_Decline_RemovesTheRequest_WithoutAnEarlierLine()
    {
        await _friends.SendRequestAsync(_bob, _alice);
        var vm = NewViewModel();
        await vm.LoadAsync();

        await ((FriendRequestRowViewModel)vm.Rows.Single()).DeclineCommand.ExecuteAsync(null);

        Assert.Empty((await _friends.GetRequestsAsync(_alice)).Incoming);
        Assert.Empty(vm.Rows);
        Assert.False(vm.HasHistory);
    }

    [Fact] public async Task ViewModel_Filters_AndMarkAllRead()
    {
        await _friends.SendRequestAsync(_bob, _alice);
        await _friends.SendRequestAsync(_cara, _alice);
        var vm = NewViewModel();
        await vm.LoadAsync();
        Assert.Equal(["cara", "bob"], vm.Rows.Select(r => r.Username));   // newest first

        vm.ShowWorkoutsCommand.Execute(null);
        Assert.Equal((true, true), (vm.IsWorkouts, vm.IsCaughtUp));
        Assert.Equal("Splits and workouts your friends share with you show up here.", vm.EmptyText);

        vm.ShowRequestsCommand.Execute(null);
        Assert.Equal(2, vm.Rows.Count);

        Assert.True(vm.MarkAllReadCommand.CanExecute(null));
        vm.MarkAllReadCommand.Execute(null);
        Assert.All(vm.Rows, r => Assert.False(r.IsNew));
        Assert.Equal("2 things waiting", vm.Subtitle);
        Assert.False(vm.MarkAllReadCommand.CanExecute(null));
    }

    [Fact] public void Store_KeepsTheNewestHistory_AndDropsOldLines()
    {
        var store = new InboxStore(_dir);
        var now = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
        store.AddHistory(_alice, new InboxHistoryEntry(InboxKind.FriendRequest, "old", now.AddDays(-31)));
        for (var i = 0; i < InboxStore.MaxHistory + 2; i++)
            store.AddHistory(_alice, new InboxHistoryEntry(InboxKind.FriendRequest, $"line {i}", now.AddMinutes(i)));

        var history = store.History(_alice, now.AddHours(1));

        Assert.Equal(InboxStore.MaxHistory, history.Count);
        Assert.Equal("line 21", history[0].Text);
        Assert.DoesNotContain(history, h => h.Text == "old");
        Assert.Empty(store.History(_bob, now));   // per user
    }

    [Fact] public void Time_ReadsLikeTheDesign()
    {
        var now = new DateTime(2026, 10, 22, 15, 0, 0, DateTimeKind.Local);
        string Ago(TimeSpan back) => InboxTime.Ago(now.Subtract(back).ToUniversalTime(), now);

        Assert.Equal("Just now", Ago(TimeSpan.FromSeconds(20)));
        Assert.Equal("12 min ago", Ago(TimeSpan.FromMinutes(12)));
        Assert.Equal("2 h ago", Ago(TimeSpan.FromHours(2)));
        Assert.Equal("Yesterday", Ago(TimeSpan.FromHours(20)));
        Assert.Equal("Mon", Ago(TimeSpan.FromDays(3)));
        Assert.Equal("14 Oct", Ago(TimeSpan.FromDays(8)));
    }
}
