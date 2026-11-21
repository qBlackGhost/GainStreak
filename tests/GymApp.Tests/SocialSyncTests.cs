using System.Net;
using GymApp.Contracts;
using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Services;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Tests;

/// <summary>Remembers every mark, for checking that each hook marks the right flag.</summary>
public sealed class RecordingSync : ISocialSyncHooks
{
    public List<string> Marks { get; } = new();
    public void MarkStatsDirty(int userId) => Marks.Add($"stats:{userId}");
    public void MarkCheckInsDirty(int userId) => Marks.Add($"checkins:{userId}");
    public void MarkSharingDirty(int userId) => Marks.Add($"sharing:{userId}");
    public void MarkAvatarDirty(int userId) => Marks.Add($"avatar:{userId}");
}

/// <summary>Data for the sync tests: a city, an OSM gym and a custom gym with a picked address.</summary>
internal sealed record GymSetup(int CityId, int OsmGymId, int CustomGymId)
{
    public static GymSetup Add(TestDb t, int ownerId)
    {
        using var db = t.CreateDbContext();
        var city = new City { Name = "Timișoara", OsmType = OsmElementType.Relation, OsmId = 7 };
        var osm = new Gym { Name = "World Class", City = city, OsmType = OsmElementType.Way, OsmId = 101, Latitude = 45.75, Longitude = 21.23 };
        var custom = new Gym { Name = "Garage gym", City = city, OwnerUserId = ownerId, Address = "Strada Mea 1", Latitude = 45.70, Longitude = 21.20 };
        db.Gyms.AddRange(osm, custom);
        db.SaveChanges();
        return new GymSetup(city.Id, osm.Id, custom.Id);
    }
}

public sealed class SocialSyncHookTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly RecordingSync _sync = new();
    private readonly int _me;

    public SocialSyncHookTests() => _me = _db.AddUser("alice");
    public void Dispose() => _db.Dispose();

    private void Log(DateOnly date, int? gymId)
    {
        using var db = _db.CreateDbContext();
        db.WorkoutLogs.Add(new WorkoutLog { UserId = _me, Date = date, GymId = gymId });
        db.SaveChanges();
    }

    [Fact] public async Task PointsSyncAndRebuild_MarkStats()
    {
        var points = new PointsService(_db, _sync);
        await points.SyncAsync(_me);
        await points.RebuildAsync(_me);
        Assert.Equal([$"stats:{_me}", $"stats:{_me}"], _sync.Marks);
    }

    [Fact] public async Task LoggingWithTheAutomaticGym_MarksCheckIns_AndWithoutAGymDoesNot()
    {
        var today = AppClock.Today;
        var gyms = GymSetup.Add(_db, _me);
        _db.ReadyToCheckIn(_me, today);
        var workouts = new WorkoutService(_db, new StreakService(_db), new PointsService(_db), _sync);

        await workouts.LogTodayAsync(_me);
        Assert.DoesNotContain($"checkins:{_me}", _sync.Marks);   // "Ask for gym" is on: no gym yet
        await workouts.UndoTodayAsync(_me);
        Assert.Equal([$"checkins:{_me}"], _sync.Marks);
        _sync.Marks.Clear();

        using (var db = _db.CreateDbContext())
        {
            db.UserProfiles.Add(new UserProfile { UserId = _me, AskForGymAfterCheckIn = false, HomeGymId = gyms.OsmGymId });
            db.SaveChanges();
        }
        await workouts.LogTodayAsync(_me);
        Assert.Equal([$"checkins:{_me}"], _sync.Marks);
    }

    [Fact] public async Task UndoingNothing_MarksNothing()
    {
        await new WorkoutService(_db, new StreakService(_db), new PointsService(_db), _sync).UndoTodayAsync(_me);
        Assert.Empty(_sync.Marks);
    }

    [Fact] public async Task SettingTodaysGym_MarksCheckIns()
    {
        var gyms = GymSetup.Add(_db, _me);
        Log(AppClock.Today, null);
        var service = new GymService(_db, _sync);

        Assert.True((await service.SetGymForTodayAsync(_me, gyms.OsmGymId)).Success);
        Assert.Equal([$"checkins:{_me}"], _sync.Marks);
    }

    [Fact] public async Task RenamingACustomGym_MarksCheckIns_OnlyIfUsedInTheLast31Days()
    {
        var gyms = GymSetup.Add(_db, _me);
        var service = new GymService(_db, _sync);
        Log(AppClock.Today.AddDays(-31), gyms.CustomGymId);   // just outside

        await service.UpdateCustomGymAsync(_me, gyms.CustomGymId, "Home gym", null, keepAddress: true);
        Assert.Empty(_sync.Marks);

        Log(AppClock.Today.AddDays(-30), gyms.CustomGymId);
        await service.UpdateCustomGymAsync(_me, gyms.CustomGymId, "Home gym", null, keepAddress: true);   // same name
        Assert.Empty(_sync.Marks);
        await service.UpdateCustomGymAsync(_me, gyms.CustomGymId, "Garage", null, keepAddress: true);
        Assert.Equal([$"checkins:{_me}"], _sync.Marks);
    }

    [Fact] public async Task Sharing_MarksSharing_AndCheckInsWhenTurnedOn()
    {
        var profiles = new ProfileService(_db, _sync);

        await profiles.SetShareGymCheckInsAsync(_me, true);
        Assert.Equal([$"sharing:{_me}", $"checkins:{_me}"], _sync.Marks);
        _sync.Marks.Clear();

        await profiles.SetShareGymCheckInsAsync(_me, false);
        Assert.Equal([$"sharing:{_me}"], _sync.Marks);
    }
}

public sealed class SocialSyncTests : IDisposable
{
    private readonly TestDb _db = TestDb.OnDisk();   // the sync writes from background tasks
    private readonly FakeSocialServer _server = new();
    private readonly SessionService _session = new();
    private readonly SocialSync _sync;
    private readonly int _me;
    private readonly GymSetup _gyms;
    private static DateOnly Today => AppClock.Today;

    public SocialSyncTests()
    {
        _me = _db.AddUser("alice");
        using (var db = _db.CreateDbContext())
        {
            var user = db.Users.Single(u => u.Id == _me);
            user.ServerUserId = 41;
            db.SaveChanges();
            _session.SignIn(user);
        }
        _gyms = GymSetup.Add(_db, _me);
        _sync = new SocialSync(_server.Api, _db, _session, new StatsProjectionService(_db));
        foreach (var route in new[] { "PUT v1/me/sharing", "PUT v1/me/checkins", "PUT v1/me/stats" })
            _server.On(route, HttpStatusCode.NoContent);
    }

    public void Dispose()
    {
        _sync.Dispose();
        _db.Dispose();
    }

    // --- The 15-minute timer: phones stop it in the background and start it again on resume (16c) ---

    [Fact] public void Timer_StartsOnce_StopsAndStartsAgain()
    {
        Assert.False(_sync.IsTimerRunning);
        _sync.StartTimer();
        _sync.StartTimer();   // resume while already running: still one timer
        Assert.True(_sync.IsTimerRunning);

        _sync.StopTimer();
        Assert.False(_sync.IsTimerRunning);
        _sync.StopTimer();   // stopping twice is harmless

        _sync.StartTimer();
        Assert.True(_sync.IsTimerRunning);
    }

    private void Share(bool? share)
    {
        using var db = _db.CreateDbContext();
        db.UserProfiles.Add(new UserProfile { UserId = _me, ShareGymCheckIns = share });
        db.SaveChanges();
    }

    private void Log(DateOnly date, int? gymId)
    {
        using var db = _db.CreateDbContext();
        db.WorkoutLogs.Add(new WorkoutLog { UserId = _me, Date = date, GymId = gymId });
        db.SaveChanges();
    }

    private SyncState State()
    {
        using var db = _db.CreateDbContext();
        return db.SyncStates.AsNoTracking().Single(s => s.UserId == _me);
    }

    private async Task MarkAllAsync()
    {
        _sync.MarkSharingDirty(_me);
        _sync.MarkCheckInsDirty(_me);
        _sync.MarkStatsDirty(_me);
        await _sync.WhenIdleAsync();
    }

    [Fact] public async Task Sync_UploadsSharingThenCheckInsThenStats_AndClearsTheFlags()
    {
        Share(true);
        Log(Today, _gyms.OsmGymId);
        _server.Offline = true;
        await MarkAllAsync();   // recorded, nothing sent
        _server.Offline = false;
        _server.Calls.Clear();

        await _sync.SyncAsync(_me);

        Assert.Equal(["PUT v1/me/sharing", "PUT v1/me/checkins", "PUT v1/me/stats", "PUT v1/me/badges"], _server.Calls);
        Assert.True(_server.LastBody<SharingRequest>("PUT v1/me/sharing")!.ShareGymCheckIns);
        var stats = _server.LastBody<StatsUploadRequest>("PUT v1/me/stats")!;
        Assert.Equal(Today, stats.Days[0].Date);
        Assert.Equal(StatsProjection.DefaultDays, stats.Days.Count);
        var state = State();
        Assert.Equal((null, null, null), (state.SharingDirtySince, state.CheckInsDirtySince, state.StatsDirtySince));
    }

    [Fact] public async Task Stats_CarryTheActiveSplit_PresetById_CustomWithoutOne()
    {
        async Task<ActiveSplitRequest?> UploadedSplitAsync(int? splitId)
        {
            using (var db = _db.CreateDbContext())
                db.Users.Where(u => u.Id == _me).ExecuteUpdate(u => u.SetProperty(x => x.SelectedSplitId, splitId));
            _sync.MarkStatsDirty(_me);
            await _sync.WhenIdleAsync();
            return _server.LastBody<StatsUploadRequest>("PUT v1/me/stats")!.Split;
        }

        int customId;
        using (var db = _db.CreateDbContext())
        {
            var custom = new WorkoutSplit { Name = "Mine", OwnerUserId = _me };
            db.WorkoutSplits.Add(custom);
            db.SaveChanges();
            customId = custom.Id;
        }

        Assert.Null(await UploadedSplitAsync(null));
        Assert.Equal(new ActiveSplitRequest(2), await UploadedSplitAsync(2));
        Assert.Equal(new ActiveSplitRequest(null), await UploadedSplitAsync(customId));
    }

    [Fact] public async Task CheckIns_AreTheLast31DaysWithAGym_ByOsmKey_AndACustomGymHasNoPosition()
    {
        Share(true);
        Log(Today, _gyms.OsmGymId);
        Log(Today.AddDays(-1), null);                  // no gym: not a check-in
        Log(Today.AddDays(-30), _gyms.CustomGymId);    // the oldest day sent
        Log(Today.AddDays(-31), _gyms.OsmGymId);       // too old

        _sync.MarkCheckInsDirty(_me);
        await _sync.WhenIdleAsync();

        var sent = _server.LastBody<CheckInsUploadRequest>("PUT v1/me/checkins")!.CheckIns;
        Assert.Equal([Today.AddDays(-30), Today], sent.Select(c => c.Date));
        Assert.Equal(new DatedCheckInRequest(Today, OsmType.Way, 101, "World Class", OsmType.Relation, 7, "Timișoara", 45.75, 21.23), sent[1]);
        Assert.Equal(new DatedCheckInRequest(Today.AddDays(-30), null, null, "Garage gym", OsmType.Relation, 7, "Timișoara", null, null), sent[0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task NotSharing_SendsFalse_AndNoCheckIns(bool? share)
    {
        Share(share);
        Log(Today, _gyms.OsmGymId);

        await MarkAllAsync();

        Assert.DoesNotContain("PUT v1/me/checkins", _server.Calls);
        Assert.False(_server.LastBody<SharingRequest>("PUT v1/me/sharing")!.ShareGymCheckIns);
        Assert.Null(State().CheckInsDirtySince);
    }

    [Fact] public async Task Offline_KeepsTheFlags_AndALaterSyncClearsThem()
    {
        Share(true);
        _server.Offline = true;
        await MarkAllAsync();

        var state = State();
        Assert.NotNull(state.SharingDirtySince);
        Assert.NotNull(state.CheckInsDirtySince);
        Assert.NotNull(state.StatsDirtySince);

        _server.Offline = false;
        await _sync.SyncAsync(_me);
        state = State();
        Assert.Equal((null, null, null), (state.SharingDirtySince, state.CheckInsDirtySince, state.StatsDirtySince));
    }

    [Fact] public async Task TooOldABuild_KeepsTheFlags_LikeOffline()
    {
        Share(true);
        foreach (var route in new[] { "PUT v1/me/sharing", "PUT v1/me/checkins", "PUT v1/me/stats" })
            _server.On(route, HttpStatusCode.UpgradeRequired, FakeSocialServer.Problem(426, "Update the app."));

        await MarkAllAsync();

        var state = State();
        Assert.NotNull(state.SharingDirtySince);
        Assert.NotNull(state.CheckInsDirtySince);
        Assert.NotNull(state.StatsDirtySince);
        Assert.Null(state.LastError);
    }

    [Fact] public async Task Refusal_ClearsTheFlag_AndRecordsTheError()
    {
        _server.On("PUT v1/me/stats", HttpStatusCode.BadRequest, FakeSocialServer.Problem(400, "The first day must be within 2 days of today."));

        _sync.MarkStatsDirty(_me);
        await _sync.WhenIdleAsync();

        var state = State();
        Assert.Null(state.StatsDirtySince);
        Assert.Equal("Stats: 400 The first day must be within 2 days of today.", state.LastError);

        _server.Calls.Clear();
        await _sync.SyncAsync(_me);
        Assert.Empty(_server.Calls);   // not retried
    }

    [Fact] public async Task CheckInsRefusedForSharing_KeepTheCheckIns_AndResendTheSettingFirst()
    {
        Share(true);
        Log(Today, _gyms.OsmGymId);
        _server.On("PUT v1/me/checkins", HttpStatusCode.Forbidden, FakeSocialServer.Problem(403, "Turn on sharing gym check-ins first."));

        _sync.MarkCheckInsDirty(_me);
        await _sync.WhenIdleAsync();
        var state = State();
        Assert.NotNull(state.CheckInsDirtySince);
        Assert.NotNull(state.SharingDirtySince);
        Assert.Null(state.LastError);

        _server.On("PUT v1/me/checkins", HttpStatusCode.NoContent);
        _server.Calls.Clear();
        await _sync.SyncAsync(_me);
        Assert.Equal(["PUT v1/me/sharing", "PUT v1/me/checkins"], _server.Calls);
        Assert.Null(State().CheckInsDirtySince);
    }

    // --- Answers that say nothing about the payload: the upload has to wait, not be dropped ---

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]      // the session ended: it goes after signing in again
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]   // the server's rate limit
    public async Task ARefusalThatIsNotAboutThePayload_KeepsTheFlags_AndALaterSyncSendsThem(HttpStatusCode status)
    {
        Share(true);
        _server.Offline = true;
        await MarkAllAsync();   // all three recorded first, so the sync below sees them together
        _server.Offline = false;
        _server.On("PUT v1/me/sharing", status);

        await _sync.SyncAsync(_me);

        var state = State();
        Assert.NotNull(state.SharingDirtySince);
        Assert.NotNull(state.CheckInsDirtySince);
        Assert.NotNull(state.StatsDirtySince);
        Assert.Null(state.LastError);
        Assert.DoesNotContain("PUT v1/me/stats", _server.Calls);   // stopped at the first step, like offline

        _server.On("PUT v1/me/sharing", HttpStatusCode.NoContent);
        await _sync.SyncAsync(_me);
        state = State();
        Assert.Equal((null, null, null), (state.SharingDirtySince, state.CheckInsDirtySince, state.StatsDirtySince));
        Assert.True(_server.LastBody<SharingRequest>("PUT v1/me/sharing")!.ShareGymCheckIns);
    }

    [Fact] public async Task OfflineHalfway_ClearsWhatWentThrough_AndKeepsTheRest()
    {
        Share(true);
        Log(Today, _gyms.OsmGymId);
        _server.Offline = true;
        await MarkAllAsync();
        _server.Offline = false;
        _server.On("PUT v1/me/checkins", _ => throw new HttpRequestException("Connection reset"));

        await _sync.SyncAsync(_me);

        var state = State();
        Assert.Null(state.SharingDirtySince);
        Assert.NotNull(state.CheckInsDirtySince);
        Assert.NotNull(state.StatsDirtySince);
        Assert.DoesNotContain("PUT v1/me/stats", _server.Calls);
    }

    [Fact] public async Task CheckInsRefusedForSharing_AreSentAgainInTheSameSync_OnceTheSettingIsThere()
    {
        Share(true);
        Log(Today, _gyms.OsmGymId);
        var sharingOnServer = false;
        _server.On("PUT v1/me/sharing", _ => { sharingOnServer = true; return FakeSocialServer.Respond(HttpStatusCode.NoContent); });
        _server.On("PUT v1/me/checkins", _ => sharingOnServer
            ? FakeSocialServer.Respond(HttpStatusCode.NoContent)
            : FakeSocialServer.Respond(HttpStatusCode.Forbidden, FakeSocialServer.Problem(403, "Turn on sharing gym check-ins first.")));

        _sync.MarkCheckInsDirty(_me);   // only the check-ins: the server lost (or never got) the setting
        await _sync.WhenIdleAsync();

        Assert.Equal(["PUT v1/me/checkins", "PUT v1/me/sharing", "PUT v1/me/checkins"], _server.Calls);
        var state = State();
        Assert.Equal((null, null, null), (state.SharingDirtySince, state.CheckInsDirtySince, state.LastError));
    }

    [Fact] public async Task CheckInsRefusedForSharing_EveryTime_GiveUpUntilTheNextSync()
    {
        Share(true);
        _server.On("PUT v1/me/checkins", HttpStatusCode.Forbidden, FakeSocialServer.Problem(403, "Turn on sharing gym check-ins first."));

        _sync.MarkCheckInsDirty(_me);
        await _sync.WhenIdleAsync();

        Assert.Equal(2, _server.Calls.Count(c => c == "PUT v1/me/checkins"));   // one retry, not a loop
        Assert.NotNull(State().CheckInsDirtySince);
    }

    // --- Signing out, restarts and marks from several threads ---

    [Fact] public async Task SigningOutDuringASync_StopsIt_AndKeepsTheRestForTheNextSignIn()
    {
        Share(true);
        Log(Today, _gyms.OsmGymId);
        _server.Offline = true;
        await MarkAllAsync();
        _server.Offline = false;
        _server.On("PUT v1/me/sharing", _ => { _session.SignOut(); return FakeSocialServer.Respond(HttpStatusCode.NoContent); });

        await _sync.SyncAsync(_me);

        Assert.Equal(["PUT v1/me/sharing"], _server.Calls);
        var state = State();
        Assert.NotNull(state.CheckInsDirtySince);
        Assert.NotNull(state.StatsDirtySince);

        // Signing in again sends them
        _server.On("PUT v1/me/sharing", HttpStatusCode.NoContent);
        using (var db = _db.CreateDbContext()) _session.SignIn(db.Users.Single(u => u.Id == _me));
        await _sync.SyncAsync(_me);
        state = State();
        Assert.Equal((null, null), (state.CheckInsDirtySince, state.StatsDirtySince));
    }

    [Fact] public async Task FlagsSurviveARestart()
    {
        Share(true);
        _server.Offline = true;
        await MarkAllAsync();
        _server.Offline = false;

        using var restarted = new SocialSync(_server.Api, _db, _session, new StatsProjectionService(_db));
        restarted.RequestSync();
        await restarted.WhenIdleAsync();

        Assert.Equal(["PUT v1/me/sharing", "PUT v1/me/checkins", "PUT v1/me/stats", "PUT v1/me/badges"], _server.Calls);
        var state = State();
        Assert.Equal((null, null, null), (state.SharingDirtySince, state.CheckInsDirtySince, state.StatsDirtySince));
    }

    [Fact] public async Task MarksFromManyThreadsAtOnce_AreAllRecorded()
    {
        _server.Offline = true;

        // The first marks for a user all want to insert the same SyncState row
        Parallel.For(0, 24, i =>
        {
            switch (i % 4)
            {
                case 0: _sync.MarkStatsDirty(_me); break;
                case 1: _sync.MarkCheckInsDirty(_me); break;
                case 2: _sync.MarkSharingDirty(_me); break;
                default: _sync.MarkAvatarDirty(_me); break;
            }
        });
        await _sync.WhenIdleAsync();

        var state = State();
        Assert.NotNull(state.StatsDirtySince);
        Assert.NotNull(state.CheckInsDirtySince);
        Assert.NotNull(state.SharingDirtySince);
        Assert.NotNull(state.AvatarDirtySince);
    }

    [Fact] public async Task SyncsAskedForAtOnce_UploadOneAtATime()
    {
        var inFlight = 0;
        var overlapped = false;
        _server.On("PUT v1/me/stats", _ =>
        {
            if (Interlocked.Increment(ref inFlight) > 1) overlapped = true;
            Thread.Sleep(20);
            Interlocked.Decrement(ref inFlight);
            return FakeSocialServer.Respond(HttpStatusCode.NoContent);
        });

        for (var i = 0; i < 6; i++) _sync.MarkStatsDirty(_me);
        for (var i = 0; i < 6; i++) _sync.RequestSync();
        await _sync.WhenIdleAsync();

        Assert.False(overlapped);
        Assert.Null(State().StatsDirtySince);
    }

    [Fact] public async Task OnlyTheSignedInLinkedUser_IsSynced()
    {
        var other = _db.AddUser("bob");
        _sync.MarkStatsDirty(other);
        await _sync.WhenIdleAsync();
        Assert.Empty(_server.Calls);   // marked, waiting for bob to sign in

        _session.SignOut();
        _sync.MarkStatsDirty(_me);
        _sync.RequestSync();
        await _sync.WhenIdleAsync();
        Assert.Empty(_server.Calls);
        Assert.NotNull(State().StatsDirtySince);
    }

    [Fact] public async Task MarkDuringAnUpload_IsNotLost()
    {
        var release = new TaskCompletionSource();
        var first = true;
        _server.On("PUT v1/me/stats", _ =>
        {
            if (first)
            {
                first = false;
                _sync.MarkStatsDirty(_me);   // a change lands while the upload is on its way
                release.Task.Wait(TimeSpan.FromSeconds(5));
            }
            return FakeSocialServer.Respond(HttpStatusCode.NoContent);
        });

        var sync = Task.Run(async () => { _sync.MarkStatsDirty(_me); await _sync.WhenIdleAsync(); });
        await Task.Delay(200);
        release.SetResult();
        await sync;
        await _sync.WhenIdleAsync();

        Assert.Equal(2, _server.Calls.Count(c => c == "PUT v1/me/stats"));
        Assert.Null(State().StatsDirtySince);
    }
}
