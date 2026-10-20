using GymApp.Contracts;
using GymApp.Core;
using GymApp.Data.Models;
using CorePeriod = GymApp.Core.LeaderboardPeriod;

namespace GymApp.Server.Tests;

/// <summary>What one phone logs reaches a friend's phone through the server. The server's clock is real time here,
/// like the phones' <see cref="AppClock"/>, so their dates agree.</summary>
public class SocialSyncEndToEndTests(PostgresFixture postgres) : ServerTest(postgres)
{
    [Fact] public async Task LoggingAWorkoutWithAGym_ReachesAFriendsLeaderboardAndMap()
    {
        var alice = await PhoneAsync("alice");
        var bob = await PhoneAsync("bob");
        await alice.Friends.SendRequestAsync(alice.Me, bob.ServerId);
        await bob.Friends.AcceptAsync(bob.Me, alice.ServerId);

        var today = AppClock.Today;
        int gymId;
        using (var db = alice.Db.CreateDbContext())
        {
            var city = new City { Name = "Timișoara", OsmType = OsmElementType.Relation, OsmId = 7 };
            var gym = new Gym { Name = "World Class", City = city, OsmType = OsmElementType.Way, OsmId = 101, Latitude = 45.75, Longitude = 21.23 };
            db.Gyms.Add(gym);
            db.SaveChanges();
            gymId = gym.Id;
        }
        alice.Db.ReadyToCheckIn(alice.Me, today);
        Assert.True((await alice.Profiles.SetShareGymCheckInsAsync(alice.Me, true)).Success);

        var log = await alice.Workouts.LogTodayAsync(alice.Me);
        Assert.True(log.Success, log.Error);
        Assert.True((await alice.Gyms.SetGymForTodayAsync(alice.Me, gymId)).Success);
        await alice.Sync.WhenIdleAsync();

        var board = (await bob.Board.GetAsync(bob.Me, CorePeriod.ThisWeek, today))!;
        var aliceRow = Assert.Single(board.Rows, r => r.Username == "alice");
        Assert.True(log.Points!.NewTotal > 0);
        Assert.Equal(log.Points.NewTotal, aliceRow.Points);   // a new account: all its points are this week's
        Assert.Equal(1, aliceRow.CurrentStreak);

        var checkIns = (await bob.Api.GetFriendCheckInsAsync(today.AddDays(-7))).Value!;
        var c = Assert.Single(checkIns);
        Assert.Equal(("alice", today, "World Class", 101L, (double?)45.75), (c.Username, c.Date, c.GymName, c.GymOsmId, c.Latitude));

        // Undo on alice's phone takes both away again
        await alice.Workouts.UndoTodayAsync(alice.Me);
        await alice.Sync.WhenIdleAsync();
        Assert.Empty((await bob.Api.GetFriendCheckInsAsync(today.AddDays(-7))).Value!);
        board = (await bob.Board.GetAsync(bob.Me, CorePeriod.ThisWeek, today))!;
        Assert.Equal(0, Assert.Single(board.Rows, r => r.Username == "alice").Points);
    }
}
