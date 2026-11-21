using GymApp.App.ViewModels;
using GymApp.App.Views;
using GymApp.Core;
using GymApp.Services;
using GymApp.Social;

namespace GymApp.Tests;

/// <summary>Phase 25e: a friend's badges on the phone.</summary>
public class FriendBadgesTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly BadgeService _badges;
    private readonly SessionService _session = new();
    private readonly int _me;

    public FriendBadgesTests()
    {
        _badges = new BadgeService(_db);
        _me = _db.AddUser("alice");
        using var db = _db.CreateDbContext();
        _session.SignIn(db.Users.Single(u => u.Id == _me));
    }

    public void Dispose() => _db.Dispose();

    private sealed class FakeShare(FriendBadges? answer) : IBadgeShareService
    {
        public Task<FriendBadges?> GetFriendBadgesAsync(int userId, int friendId) => Task.FromResult(answer);
        public Task<int?> GetAcceptedInvitesAsync(int userId) => Task.FromResult<int?>(0);
    }

    private async Task<FriendBadgesViewModel> OpenAsync(FriendBadges? theirs)
    {
        var vm = new FriendBadgesViewModel(new FakeShare(theirs), _badges, _session, () => null!);
        await vm.LoadAsync(7, "mara", null, 15, null);
        return vm;
    }

    private sealed class FakeBoard(params LeaderboardRow[] rows) : ILeaderboardService
    {
        public Task<LeaderboardView?> GetAsync(int userId, LeaderboardPeriod period, DateOnly? today = null) =>
            Task.FromResult<LeaderboardView?>(new LeaderboardView(period, default, default, rows));
    }

    [Fact] public async Task OpenedWithoutALevel_TakesLevelAndAvatarFromTheBoard()
    {
        var board = new FakeBoard(new LeaderboardRow(1, 7, "mara", 4820, LevelCalculator.For(4820), 23, false, "a:3:1"));
        var vm = new FriendBadgesViewModel(new FakeShare(Mara()), _badges, _session, () => null!, board);
        await vm.LoadAsync(7, "mara", null, 0, null);
        Assert.Equal(LevelCalculator.For(4820).Number, vm.Level);
        Assert.Equal("a:3:1", vm.Avatar);

        var known = new FriendBadgesViewModel(new FakeShare(Mara()), _badges, _session, () => null!, board);
        await known.LoadAsync(7, "mara", null, 15, null);
        Assert.Equal(15, known.Level);   // a caller that knows the level keeps it
        Assert.Null(known.Avatar);
    }

    private static FriendBadges Mara(bool stale = false, params SharedBadge[] badges) =>
        new(7, badges, "streak") { IsStale = stale };

    [Fact] public async Task Notes_CompareWithYours_AndHiddenOnesStaySecret()
    {
        await _badges.AwardAsync(_me, "streak", 3);
        await _badges.AwardAsync(_me, "century", 1);
        var vm = await OpenAsync(Mara(false,
            new("streak", 2, new(2026, 10, 1)), new("century", 1, new(2026, 10, 1)),
            new("explorer", 0, new(2026, 10, 1)), new("night-owl", 0, new(2026, 10, 1))));

        var notes = vm.Shown.ToDictionary(t => t.Item.Id, t => t.Note);
        Assert.Equal("You: Fire", notes["streak"]);   // a tiered badge always names your tier, higher or not
        Assert.Equal($"You: {BadgeCatalog.TierName(BadgeCatalog.Find("century")!, 1)}", notes["century"]);   // even when it's the same
        Assert.Equal("You: no", notes["explorer"]);
        Assert.Equal(BadgeCatalog.Find("night-owl")!.Hint, notes["night-owl"]);   // a hidden one you lack: its hint
        var explorer = vm.Shown.Single(t => t.Item.Id == "explorer");
        Assert.True(explorer.ShowsCross);
        Assert.Equal("You:", explorer.Label);
        var owl = vm.Shown.Single(t => t.Item.Id == "night-owl").Item;
        Assert.True(owl.IsMasked);
        Assert.Equal(BadgeLook.Hidden, owl.Look);
        Assert.Equal("4 / 39", vm.CountText);
        Assert.Equal("2 in common", vm.CommonText);
        Assert.Equal("streak", vm.Featured!.Id);
    }

    [Fact] public async Task OneTimeBadges_ShowACheckOrACross_AndTieredOnesYouLackShowACross()
    {
        await _badges.AwardAsync(_me, "explorer", 0);
        var vm = await OpenAsync(Mara(false, new("explorer", 0, new(2026, 10, 1)), new("century", 2, new(2026, 10, 1))));

        var explorer = vm.Shown.Single(t => t.Item.Id == "explorer");
        Assert.True(explorer.ShowsCheck);
        Assert.False(explorer.ShowsCross);
        Assert.Equal("You: yes", explorer.Note);
        var century = vm.Shown.Single(t => t.Item.Id == "century");
        Assert.True(century.ShowsCross);
        Assert.Equal("You: no", century.Note);
    }

    [Fact] public void AFeaturedHiddenBadge_IsASecretUntilYouHaveIt()
    {
        var secret = FeaturedLook.From("night-owl:0");   // nothing known about the viewer: kept secret
        Assert.NotNull(secret);
        Assert.True(secret!.IsSecret);
        Assert.Equal(FeaturedLook.SecretName, secret.Name);
        Assert.Equal("?", secret.Text);
        Assert.Equal(BadgeLook.Secret, secret.Look);
        Assert.True(FeaturedLook.From("night-owl:0", _ => false)!.IsSecret);

        var yours = FeaturedLook.From("night-owl:0", id => id == "night-owl")!;
        Assert.False(yours.IsSecret);
        Assert.Equal("Night Owl", yours.Name);
        Assert.False(FeaturedLook.From("streak:3")!.IsSecret);   // visible badges never hide
        Assert.Null(FeaturedLook.From("future-badge-x:2"));      // an id this build doesn't know: no badge
    }

    [Fact] public async Task ThePicker_WarnsWhenYouFeatureASecretBadge()
    {
        await _badges.AwardAsync(_me, "night-owl", 0);
        await _badges.AwardAsync(_me, "streak", 2);
        var picker = new FeaturedBadgePickerViewModel(_badges, _session, () => null!);
        await picker.LoadAsync("A", null, () => Task.CompletedTask);

        picker.SelectCommand.Execute(picker.Choices.Single(c => c.Item?.Id == "night-owl"));
        Assert.True(picker.IsSecretPick);
        Assert.Contains("\"?\"", picker.SecretNote);
        picker.SelectCommand.Execute(picker.Choices.Single(c => c.Item?.Id == "streak"));
        Assert.False(picker.IsSecretPick);
        picker.SelectCommand.Execute(picker.Choices.Single(c => c.Item is null));   // None
        Assert.False(picker.IsSecretPick);
    }

    [Fact] public async Task AFriendsHiddenFeaturedBadge_ShowsAsSecret_OnTheirScreen_UntilYouEarnIt()
    {
        var theirs = new FriendBadges(7, [new SharedBadge("night-owl", 0, new(2026, 11, 1))], "night-owl");
        var vm = new FriendBadgesViewModel(new FakeShare(theirs), _badges, _session, () => null!);
        await vm.LoadAsync(7, "mara", null, 15, "night-owl:0");
        Assert.True(vm.Featured!.IsSecret);

        await _badges.AwardAsync(_me, "night-owl", 0);
        var after = new FriendBadgesViewModel(new FakeShare(theirs), _badges, _session, () => null!);
        await after.LoadAsync(7, "mara", null, 15, "night-owl:0");
        Assert.False(after.Featured!.IsSecret);
        Assert.Equal("Night Owl", after.Featured.Name);
    }

    [Fact] public async Task TheToggle_FiltersBothAndOnlyTheirs()
    {
        await _badges.AwardAsync(_me, "century", 1);
        var vm = await OpenAsync(Mara(false, new("century", 1, new(2026, 10, 1)), new("explorer", 0, new(2026, 10, 1))));

        vm.ShowBothCommand.Execute(null);
        Assert.Equal(["century"], vm.Shown.Select(t => t.Item.Id));
        vm.ShowOnlyCommand.Execute(null);
        Assert.Equal(["explorer"], vm.Shown.Select(t => t.Item.Id));
        Assert.Equal("Only mara", vm.OnlyLabel);
    }

    [Fact] public async Task Offline_SaysSo_AndNothing_SaysItCouldntLoad()
    {
        Assert.Equal("Offline: showing what was last fetched.", (await OpenAsync(Mara(true, new SharedBadge("century", 1, new(2026, 10, 1))))).Notice);
        var none = await OpenAsync(null);
        Assert.StartsWith("Couldn't load mara's badges", none.Notice);
        Assert.True(none.IsEmpty);
    }

    [Fact] public void FeaturedLook_FromTheWireValue()
    {
        var heavy = FeaturedLook.From("heavy-lifter:3")!;
        Assert.Equal(("whale", BadgeLook.Fire), (heavy.Glyph, heavy.Look));
        Assert.Equal(BadgeLook.Ice, FeaturedLook.From("on-ice:0")!.Look);
        Assert.Null(FeaturedLook.From("from-a-newer-app:2"));
        Assert.Null(FeaturedLook.From(null));
    }
}
