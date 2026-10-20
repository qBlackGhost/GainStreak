using GymApp.Client;
using GymApp.Services;
using GymApp.Tests;

namespace GymApp.Server.Tests;

/// <summary>One phone talking to the test server: its own SQLite, session and token, and the app's real services.</summary>
public sealed class Phone : IDisposable
{
    public Phone(Func<ITokenStore, SocialApiClient> api)
    {
        Api = api(Tokens);
        Accounts = new AccountService(Api, Db, Tokens, Session, "test phone");
        var cache = new SocialCacheStore(Db);
        Friends = new RemoteFriendService(Api, cache, Session);
        Board = new RemoteLeaderboardService(Api, cache, Session);
        Sync = new SocialSync(Api, Db, Session, new StatsProjectionService(Db));
        Points = new PointsService(Db, Sync);
        Workouts = new WorkoutService(Db, new StreakService(Db), Points, Sync);
        Gyms = new GymService(Db, Sync);
        Profiles = new ProfileService(Db, Sync);
    }

    /// <summary>On disk: the sync writes from background tasks.</summary>
    public TestDb Db { get; } = TestDb.OnDisk();
    public SessionService Session { get; } = new();
    public MemoryTokenStore Tokens { get; } = new();
    public SocialApiClient Api { get; }
    public AccountService Accounts { get; }
    public RemoteFriendService Friends { get; }
    public RemoteLeaderboardService Board { get; }
    public SocialSync Sync { get; }
    public PointsService Points { get; }
    public WorkoutService Workouts { get; }
    public GymService Gyms { get; }
    public ProfileService Profiles { get; }

    /// <summary>The signed-in user's local id: what the view models pass as <c>userId</c>.</summary>
    public int Me => Session.CurrentUser!.Id;

    /// <summary>The signed-in user's server id: what other phones see in their rows.</summary>
    public int ServerId => Session.CurrentUser!.ServerUserId!.Value;

    public void Dispose()
    {
        Sync.WhenIdleAsync().GetAwaiter().GetResult();
        Sync.Dispose();
        Db.Dispose();
    }
}
