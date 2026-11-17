using System.Net;
using GymApp.Client;
using GymApp.Contracts;
using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Services;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Tests;

/// <summary>Custom splits: creating, editing and deleting them, and what editing the active one does to the week.</summary>
public sealed class SplitServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly RecordingSync _sync = new();
    private readonly SplitService _splits;
    private readonly int _me;

    public SplitServiceTests()
    {
        _me = _db.AddUser("alice");
        _splits = new SplitService(_db, new PointsService(_db), _sync);
    }

    public void Dispose() => _db.Dispose();

    private static DayInput Day(DayOfWeek weekday, string name) => new(weekday, name);

    private async Task<WorkoutSplit> CreateAsync(string name, params DayInput[] days)
    {
        var r = await _splits.CreateAsync(_me, name, days);
        Assert.True(r.Success, r.Error);
        return (await _splits.GetCustomAsync(_me)).Single(s => s.Name == name.Trim());
    }

    private string? CurrentWeek()
    {
        using var db = _db.CreateDbContext();
        return db.UserSchedules.Where(s => s.UserId == _me).OrderBy(s => s.EffectiveFrom).Select(s => s.WeekPattern).LastOrDefault();
    }

    [Fact] public async Task Create_TrimsNames_AndOrdersTheDaysFromMonday()
    {
        var split = await CreateAsync("  My week ", Day(DayOfWeek.Sunday, " Legs "), Day(DayOfWeek.Wednesday, "Pull"), Day(DayOfWeek.Monday, "Push"));

        Assert.Equal("My week", split.Name);
        Assert.Equal([(DayOfWeek.Monday, "Push", 1), (DayOfWeek.Wednesday, "Pull", 2), (DayOfWeek.Sunday, "Legs", 3)],
            split.Days.Select(d => (d.Weekday!.Value, d.Name, d.Order)));
    }

    [Fact] public async Task Create_RefusesANameAlreadyUsed_WhateverTheCase_ButNotSomeoneElses()
    {
        await CreateAsync("Upper Lower", Day(DayOfWeek.Monday, "Upper"));

        var again = await _splits.CreateAsync(_me, "upper lower", [Day(DayOfWeek.Tuesday, "Lower")]);
        Assert.Equal((false, "You already have a split with that name.", FormField.Name), (again.Success, again.Error, again.Field));

        var bob = _db.AddUser("bob");
        Assert.True((await _splits.CreateAsync(bob, "Upper Lower", [Day(DayOfWeek.Monday, "Upper")])).Success);
    }

    [Fact] public async Task Create_StopsAtTheLimit()
    {
        for (var i = 1; i <= SplitService.MaxCustomSplits; i++)
            await CreateAsync($"Split {i}", Day(DayOfWeek.Monday, "Day"));

        var over = await _splits.CreateAsync(_me, "One more", [Day(DayOfWeek.Monday, "Day")]);

        Assert.Equal((false, "You can have at most 5 custom splits."), (over.Success, over.Error));
        Assert.Equal(SplitService.MaxCustomSplits, (await _splits.GetCustomAsync(_me)).Count);
    }

    [Theory]
    [InlineData("A name that is far longer than forty characters", "Push", "Split name must be at most 40 characters.")]
    [InlineData("PPL", "A day name over twenty", "Day names must be at most 20 characters.")]
    public async Task Create_RefusesNamesThatAreTooLong(string name, string day, string error)
    {
        var r = await _splits.CreateAsync(_me, name, [Day(DayOfWeek.Monday, day)]);
        Assert.Equal((false, error), (r.Success, r.Error));
    }

    [Fact] public async Task Create_RefusesAWeekdayPickedTwice()
    {
        var r = await _splits.CreateAsync(_me, "PPL", [Day(DayOfWeek.Monday, "Push"), Day(DayOfWeek.Monday, "Pull")]);
        Assert.Equal((false, "A weekday was selected twice.", FormField.Days), (r.Success, r.Error, r.Field));
    }

    [Fact] public async Task Update_KeepsADaysExercisesWhenItIsRenamed_AndDropsARemovedDaysOnes()
    {
        var split = await CreateAsync("PPL", Day(DayOfWeek.Monday, "Push"), Day(DayOfWeek.Wednesday, "Pull"));
        var monday = split.Days.Single(d => d.Weekday == DayOfWeek.Monday).Id;
        var wednesday = split.Days.Single(d => d.Weekday == DayOfWeek.Wednesday).Id;
        _db.AddDayExercises(_me, monday, 2);
        _db.AddDayExercises(_me, wednesday, 3);

        var r = await _splits.UpdateAsync(_me, split.Id, "PPL v2",
            [Day(DayOfWeek.Friday, "Legs"), Day(DayOfWeek.Monday, "Chest day")]);

        Assert.True(r.Success, r.Error);
        var updated = (await _splits.GetCustomAsync(_me)).Single();
        Assert.Equal("PPL v2", updated.Name);
        Assert.Equal([(DayOfWeek.Monday, "Chest day", 1), (DayOfWeek.Friday, "Legs", 2)],
            updated.Days.Select(d => (d.Weekday!.Value, d.Name, d.Order)));
        Assert.Equal(monday, updated.Days[0].Id);   // the same day, so its exercises stay
        using var db = _db.CreateDbContext();
        Assert.Equal(2, db.UserDayExercises.Count(x => x.SplitDayId == monday));
        Assert.Equal(0, db.UserDayExercises.Count(x => x.SplitDayId == wednesday));
    }

    [Fact] public async Task Update_RefusesAnotherSplitsName_ButKeepsItsOwn()
    {
        await CreateAsync("Upper Lower", Day(DayOfWeek.Monday, "Upper"));
        var ppl = await CreateAsync("PPL", Day(DayOfWeek.Monday, "Push"));

        var taken = await _splits.UpdateAsync(_me, ppl.Id, "UPPER LOWER", [Day(DayOfWeek.Monday, "Push")]);
        Assert.Equal((false, FormField.Name), (taken.Success, taken.Field));

        Assert.True((await _splits.UpdateAsync(_me, ppl.Id, "ppl", [Day(DayOfWeek.Monday, "Push")])).Success);
    }

    [Fact] public async Task Update_OnlyTouchesYourOwnSplits_AndValidatesLikeCreate()
    {
        var split = await CreateAsync("PPL", Day(DayOfWeek.Monday, "Push"));
        var bob = _db.AddUser("bob");

        var notMine = await _splits.UpdateAsync(bob, split.Id, "Stolen", [Day(DayOfWeek.Monday, "Push")]);
        var noDays = await _splits.UpdateAsync(_me, split.Id, "PPL", []);

        Assert.Equal((false, "Split not found."), (notMine.Success, notMine.Error));
        Assert.Equal((false, FormField.Days), (noDays.Success, noDays.Field));
        Assert.Equal("PPL", (await _splits.GetCustomAsync(_me)).Single().Name);
    }

    [Fact] public async Task Update_OfTheActiveSplit_ChangesTheWeekFromToday_AndAnInactiveOneDoesNot()
    {
        var active = await CreateAsync("PPL", Day(DayOfWeek.Monday, "Push"), Day(DayOfWeek.Wednesday, "Pull"));
        var other = await CreateAsync("Other", Day(DayOfWeek.Sunday, "Full body"));
        Assert.True(await _splits.SelectAsync(_me, active.Id, null, null));
        var before = CurrentWeek();
        Assert.NotNull(before);

        await _splits.UpdateAsync(_me, other.Id, "Other", [Day(DayOfWeek.Saturday, "Full body")]);
        Assert.Equal(before, CurrentWeek());

        await _splits.UpdateAsync(_me, active.Id, "PPL", [Day(DayOfWeek.Monday, "Push"), Day(DayOfWeek.Friday, "Legs")]);
        Assert.Equal(WeekPattern.From(WeekSchedule.FromWeekdays([DayOfWeek.Monday, DayOfWeek.Friday])), CurrentWeek());
        using var db = _db.CreateDbContext();
        Assert.Equal(1, db.UserSchedules.Count(s => s.UserId == _me));   // twice in a day is still one row
    }

    [Fact] public async Task Select_RefusesSomeoneElsesSplit_AndAPresetWithoutAVariant()
    {
        var bob = _db.AddUser("bob");
        var mine = await CreateAsync("PPL", Day(DayOfWeek.Monday, "Push"));
        var preset = (await _splits.GetPresetsAsync())[0];

        Assert.False(await _splits.SelectAsync(bob, mine.Id, null, null));
        Assert.False(await _splits.SelectAsync(_me, preset.Id, null, DayOfWeek.Monday));
        Assert.False(await _splits.SelectAsync(_me, preset.Id, -1, DayOfWeek.Monday));
        Assert.True(await _splits.SelectAsync(_me, preset.Id, preset.Variants[0].Id, null));   // starts on Monday by default
        using var db = _db.CreateDbContext();
        var user = db.Users.Single(u => u.Id == _me);
        Assert.Equal((preset.Id, DayOfWeek.Monday), (user.SelectedSplitId, user.SplitStartDay));
    }

    [Fact] public async Task Delete_RemovesOnlyYourOwn_AndMarksStatsWhenItWasTheActiveOne()
    {
        var active = await CreateAsync("PPL", Day(DayOfWeek.Monday, "Push"));
        var other = await CreateAsync("Other", Day(DayOfWeek.Sunday, "Full body"));
        await _splits.SelectAsync(_me, active.Id, null, null);
        var bob = _db.AddUser("bob");
        _sync.Marks.Clear();

        await _splits.DeleteAsync(bob, other.Id);   // not bob's
        Assert.Equal(2, (await _splits.GetCustomAsync(_me)).Count);

        await _splits.DeleteAsync(_me, other.Id);
        Assert.Empty(_sync.Marks);   // the upload carries the active split only

        await _splits.DeleteAsync(_me, active.Id);
        Assert.Equal([$"stats:{_me}"], _sync.Marks);
        Assert.Empty(await _splits.GetCustomAsync(_me));
        using var db = _db.CreateDbContext();
        Assert.Null(db.Users.Single(u => u.Id == _me).SelectedSplitId);
    }
}

/// <summary>The app's side of two small server features: the split usage chart's numbers and shares.</summary>
public sealed class RemoteSplitUsageAndShareTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly FakeSocialServer _server = new();
    private readonly SessionService _session = new();
    private readonly int _me;

    public RemoteSplitUsageAndShareTests()
    {
        _me = _db.AddUser("alice");
        using var db = _db.CreateDbContext();
        _session.SignIn(db.Users.Single(u => u.Id == _me));
    }

    public void Dispose() => _db.Dispose();

    [Fact] public async Task SplitUsage_ComesFromTheServer_ThenFromTheLastFetchOffline_AndIsNullWithNone()
    {
        var usage = new RemoteSplitUsageService(_server.Api, new SocialCacheStore(_db), _session);
        _server.Offline = true;
        Assert.Null(await usage.GetAsync(_me));

        _server.Offline = false;
        _server.On("GET v1/splits/usage", HttpStatusCode.OK, new SplitUsageResponse(5, [new(2, 3)], 2));
        var fresh = (await usage.GetAsync(_me))!;
        Assert.Equal((5, 2, 3), (fresh.Users, fresh.Custom, fresh.Presets.Single().Users));

        _server.Offline = true;
        var cached = (await usage.GetAsync(_me))!;
        Assert.Equal((5, 2, 3), (cached.Users, cached.Custom, cached.Presets.Single().Users));

        await Assert.ThrowsAsync<InvalidOperationException>(() => usage.GetAsync(_me + 1));
    }

    private static ShareRequest Workout(params int[] to) =>
        new(to, null, new SharedWorkout("Push", "PPL", [new SharedExercise("Bench Press", "Chest", false, 1, 3)]));

    [Fact] public async Task Share_IsCheckedBeforeSending_AndCarriesTheServersAnswer()
    {
        var shares = new RemoteShareService(_server.Api);

        var nobody = await shares.SendAsync(Workout());
        Assert.Equal((false, ShareRules.NoRecipientsMessage), (nobody.Success, nobody.Error));
        Assert.Empty(_server.Calls);   // refused on the phone

        _server.On("POST v1/shares", HttpStatusCode.OK, new SendShareResponse(2));
        var sent = await shares.SendAsync(Workout(901, 902));
        Assert.Equal((true, 2), (sent.Success, sent.Sent));

        _server.On("POST v1/shares", HttpStatusCode.BadRequest, FakeSocialServer.Problem(400, ShareRules.NotFriendsMessage));
        Assert.Equal(ShareRules.NotFriendsMessage, (await shares.SendAsync(Workout(903))).Error);

        _server.Offline = true;
        Assert.Equal(ApiResult.OfflineMessage, (await shares.SendAsync(Workout(901))).Error);
    }

    [Fact] public async Task Inbox_ListsShares_IsNullOffline_AndRemovingOneAlreadyGoneIsFine()
    {
        var shares = new RemoteShareService(_server.Api);
        var sentAt = new DateTime(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc);
        _server.On("GET v1/shares", HttpStatusCode.OK, new List<ShareResponse> { new(7, 901, "bob", sentAt, null, Workout(1).Workout) });
        _server.On("DELETE v1/shares/7", HttpStatusCode.NoContent);
        _server.On("DELETE v1/shares/8", HttpStatusCode.NotFound, FakeSocialServer.Problem(404, "That share is gone."));
        _server.On("DELETE v1/shares/9", HttpStatusCode.TooManyRequests);

        var share = Assert.Single((await shares.GetIncomingAsync())!);
        Assert.Equal((7L, "bob", "Push"), (share.Id, share.FromUsername, share.Workout!.DayName));
        Assert.Null(await shares.RemoveAsync(7));
        Assert.Null(await shares.RemoveAsync(8));
        Assert.Equal(ApiResult.TooManyMessage, await shares.RemoveAsync(9));

        _server.Offline = true;
        Assert.Null(await shares.GetIncomingAsync());
        Assert.Equal(ApiResult.OfflineMessage, await shares.RemoveAsync(7));
    }
}
