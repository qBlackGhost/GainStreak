using System;
using System.IO;
using System.Net.Http;
using GymApp.Client;
using GymApp.Data;
using GymApp.Desktop.ViewModels;
using GymApp.Services;
using GymApp.Social;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GymApp.Desktop;

/// <summary>The app's services, apart from <see cref="App"/> so a test can build them in both modes without a window.</summary>
public static class AppServices
{
    /// <param name="localSocial">Accounts and friends on this computer only (the TestBench's local mode) instead of the
    /// social server.</param>
    public static ServiceCollection Create(bool localSocial)
    {
        var sc = new ServiceCollection();
        sc.AddDbContextFactory<AppDbContext>();
        sc.AddSingleton<SessionService>();
        if (localSocial)
        {
            sc.AddTransient<AuthService>();
            sc.AddSingleton<RememberMeService>();
            sc.AddSingleton<IAccountService, LocalAccountService>();
            sc.AddTransient<ISocialStore, LocalSocialStore>();
            sc.AddTransient<IFriendCheckInSource, LocalSocialStore>();
            sc.AddTransient<IFriendService, FriendService>();
            sc.AddTransient<ILeaderboardService, LeaderboardService>();
        }
        else
        {
            // The session token is the stored credential: a file only this user can read
            sc.AddSingleton<ITokenStore>(_ => new FileTokenStore(Path.Combine(AppDbContext.DataDirectory, "session.token")));
            sc.AddSingleton(sp => new SocialApiClient(
                new HttpClient { BaseAddress = ServerAddress.FromEnvironment() }, sp.GetRequiredService<ITokenStore>()));
            sc.AddSingleton(sp => new AccountService(sp.GetRequiredService<SocialApiClient>(),
                sp.GetRequiredService<IDbContextFactory<AppDbContext>>(), sp.GetRequiredService<ITokenStore>(),
                sp.GetRequiredService<SessionService>()));
            sc.AddSingleton<IAccountService>(sp => sp.GetRequiredService<AccountService>());
            sc.AddTransient(sp => new SocialCacheStore(sp.GetRequiredService<IDbContextFactory<AppDbContext>>()));
            sc.AddTransient<IFriendService, RemoteFriendService>();
            sc.AddTransient<ILeaderboardService, RemoteLeaderboardService>();
            sc.AddTransient<IFriendCheckInSource, RemoteFriendCheckInSource>();
            // Uploads stats, check-ins and the sharing setting; the services mark what changed through the hooks
            sc.AddTransient(sp => new StatsProjectionService(sp.GetRequiredService<IDbContextFactory<AppDbContext>>()));
            sc.AddSingleton(sp => new SocialSync(sp.GetRequiredService<SocialApiClient>(),
                sp.GetRequiredService<IDbContextFactory<AppDbContext>>(), sp.GetRequiredService<SessionService>(),
                sp.GetRequiredService<StatsProjectionService>()));
            sc.AddSingleton<ISocialSyncHooks>(sp => sp.GetRequiredService<SocialSync>());
        }
        sc.AddSingleton<MainViewModel>();
        sc.AddSingleton<Func<MainViewModel>>(sp => () => sp.GetRequiredService<MainViewModel>());
        sc.AddTransient<LoginViewModel>();
        sc.AddTransient<RegisterViewModel>();
        sc.AddTransient<HomeViewModel>();
        sc.AddTransient<SplitService>();
        sc.AddTransient<SplitsViewModel>();
        sc.AddTransient<SplitEditorViewModel>();
        sc.AddTransient<SplitDetailViewModel>();
        sc.AddTransient<StreakService>();
        sc.AddTransient<PointsService>();
        sc.AddTransient<WorkoutService>();
        sc.AddTransient<MissedDayService>();
        sc.AddTransient<ExerciseService>();
        sc.AddTransient<DayExerciseService>();
        sc.AddTransient<SetService>();
        sc.AddTransient<WorkoutMenuService>();
        sc.AddTransient<WorkoutsViewModel>();
        sc.AddTransient<DayViewModel>();
        sc.AddTransient<ExerciseViewModel>();
        sc.AddTransient<CustomExercisesViewModel>();
        sc.AddTransient<ProfileService>();
        sc.AddTransient<CalorieService>();
        sc.AddTransient<ProfileViewModel>();
        sc.AddTransient<CaloriesViewModel>();
        sc.AddSingleton(_ => new GeocodingService());   // one instance, so its one-request-per-second limit holds
        sc.AddTransient<CityService>();
        sc.AddSingleton(sp => new GymFetchService(sp.GetRequiredService<IDbContextFactory<AppDbContext>>()));
        sc.AddTransient<GymService>();
        sc.AddSingleton(sp => new GymAddressLookupService(
            sp.GetRequiredService<IDbContextFactory<AppDbContext>>(), sp.GetRequiredService<GeocodingService>()));
        sc.AddTransient<GymPickerViewModel>();
        sc.AddTransient<GymSummaryService>();
        sc.AddTransient<GymsViewModel>();
        sc.AddTransient<PointsHistoryViewModel>();
        sc.AddTransient<FriendMapService>();
        sc.AddTransient<FriendsViewModel>();
        return sc;
    }
}
