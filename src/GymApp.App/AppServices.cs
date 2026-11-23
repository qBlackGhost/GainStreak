using System;
using System.Net.Http;
using GymApp.Client;
using GymApp.Data;
using GymApp.App.ViewModels;
using GymApp.Services;
using GymApp.Social;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GymApp.App;

/// <summary>The app's services, apart from <see cref="GymApplication"/> so a test can build them in both modes without a window.</summary>
public static class AppServices
{
    /// <param name="localSocial">Accounts and friends on this computer only (the TestBench's local mode) instead of the
    /// social server.</param>
    /// <param name="platform">The head: its data folder, token store and server address.</param>
    public static ServiceCollection Create(bool localSocial, IPlatform platform)
    {
        var sc = new ServiceCollection();
        sc.AddSingleton(platform);
        AppHttp.UserAgent = AppHttp.ForPlatform(AppVersion.Current, AppVersion.PlatformName);
        sc.AddDbContextFactory<AppDbContext>();
        sc.AddSingleton(_ => new ErrorLog(platform.DataDirectory, appInfo: platform.AppVersionDescription));
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
            sc.AddTransient<IFeedbackService, LocalFeedbackService>();
            sc.AddTransient<ISplitUsageService, LocalSplitUsageService>();
            sc.AddTransient<IShareService, LocalShareService>();
            sc.AddTransient<IUserReportService, LocalUserReportService>();
            sc.AddTransient<IBadgeShareService, LocalBadgeShareService>();
            sc.AddSingleton<IPushRegistration>(sp => new LocalPushRegistration(sp.GetRequiredService<IPushNotifications>(),
                sp.GetRequiredService<IDbContextFactory<AppDbContext>>(), sp.GetRequiredService<SessionService>(), sp.GetRequiredService<ErrorLog>()));
        }
        else
        {
            // The session token is the stored credential: kept where only this user (or app) can read it
            sc.AddSingleton(_ => platform.CreateTokenStore());
            sc.AddSingleton(sp => new SocialApiClient(
                new HttpClient { BaseAddress = ServerAddress.FromEnvironment(platform.ServerUrl) }, sp.GetRequiredService<ITokenStore>(),
                appVersion: AppVersion.Current));
            sc.AddSingleton<IServerGyms>(sp => new RemoteServerGyms(sp.GetRequiredService<SocialApiClient>()));
            sc.AddSingleton(sp => new AccountService(sp.GetRequiredService<SocialApiClient>(),
                sp.GetRequiredService<IDbContextFactory<AppDbContext>>(), sp.GetRequiredService<ITokenStore>(),
                sp.GetRequiredService<SessionService>()));
            sc.AddSingleton<IAccountService>(sp => sp.GetRequiredService<AccountService>());
            sc.AddTransient(sp => new SocialCacheStore(sp.GetRequiredService<IDbContextFactory<AppDbContext>>()));
            sc.AddTransient<IFriendService, RemoteFriendService>();
            sc.AddTransient<ILeaderboardService, RemoteLeaderboardService>();
            sc.AddTransient<IFriendCheckInSource, RemoteFriendCheckInSource>();
            sc.AddTransient<IFeedbackService, RemoteFeedbackService>();
            sc.AddTransient<ISplitUsageService, RemoteSplitUsageService>();
            sc.AddTransient<IShareService, RemoteShareService>();
            sc.AddTransient<IUserReportService, RemoteUserReportService>();
            sc.AddTransient<IBadgeShareService, RemoteBadgeShareService>();
            // Uploads stats, check-ins and the sharing setting; the services mark what changed through the hooks
            sc.AddTransient(sp => new StatsProjectionService(sp.GetRequiredService<IDbContextFactory<AppDbContext>>()));
            sc.AddSingleton(sp => new SocialSync(sp.GetRequiredService<SocialApiClient>(),
                sp.GetRequiredService<IDbContextFactory<AppDbContext>>(), sp.GetRequiredService<SessionService>(),
                sp.GetRequiredService<StatsProjectionService>()));
            // Friend notifications (Phase 22): the server's copy of this phone's push token
            sc.AddSingleton<IPushRegistration>(sp => new RemotePushRegistration(sp.GetRequiredService<SocialApiClient>(),
                sp.GetRequiredService<ITokenStore>(), sp.GetRequiredService<IPushNotifications>(),
                sp.GetRequiredService<IDbContextFactory<AppDbContext>>(), sp.GetRequiredService<SessionService>(),
                sp.GetRequiredService<ErrorLog>()));
            // The reminders replan on the same changes (16g)
            sc.AddSingleton<ISocialSyncHooks>(sp => new ReminderHooks(sp.GetRequiredService<SocialSync>(),
                sp.GetRequiredService<ReminderSync>(), sp.GetRequiredService<WidgetSync>, sp.GetRequiredService<BadgeSync>));
        }
        // Reminders (16g): planned here, scheduled by the head
        sc.AddSingleton(_ => platform.CreateNotificationScheduler());
        sc.AddSingleton(_ => platform.CreateBarcodeScanner());   // the food log's camera (Phase 26)
        sc.AddSingleton(_ => platform.CreateLocationProvider());   // "verified at gym" (18c)
        sc.AddSingleton(_ => platform.CreateLiveActivity());   // the workout's Dynamic Island on iOS (0.20.0)
        sc.AddSingleton(_ => platform.CreateHealthStore());   // Apple Health (Phase 20)
        sc.AddSingleton(_ => platform.CreatePushNotifications());   // friend notifications (Phase 22)
        sc.AddTransient<HealthExportService>();
        sc.AddSingleton<IHealthSetting>(sp => new ProfileHealthSetting(sp.GetRequiredService<ProfileService>()));
        sc.AddTransient<HealthImportService>();
        sc.AddSingleton(sp => new HealthSync(sp.GetRequiredService<IHealthStore>(), sp.GetRequiredService<HealthExportService>(),
            sp.GetRequiredService<SessionService>(), sp.GetRequiredService<IHealthSetting>(), sp.GetRequiredService<ErrorLog>(),
            import: sp.GetRequiredService<HealthImportService>()));
        sc.AddTransient(sp => new ReminderService(sp.GetRequiredService<IDbContextFactory<AppDbContext>>()));
        sc.AddSingleton<ReminderSync>();
        // The iPhone's Home Screen widgets (Phase 24): built here, saved and redrawn by the head
        sc.AddSingleton(_ => platform.CreateHomeWidgets());
        sc.AddTransient<WidgetSnapshotService>();
        sc.AddSingleton<WidgetSync>();
        // Badges (Phase 25): recomputed after anything logged, stored, and their unlocks shown
        sc.AddTransient(sp => new BadgeService(sp.GetRequiredService<IDbContextFactory<AppDbContext>>()));
        sc.AddSingleton<BadgeSync>();
        sc.AddTransient<BadgesViewModel>();
        sc.AddTransient<BadgeDetailViewModel>();
        sc.AddTransient<BadgeUnlockViewModel>();
        sc.AddTransient<FeaturedBadgePickerViewModel>();
        sc.AddTransient<ProfileBadgesViewModel>();
        sc.AddTransient<FriendBadgesViewModel>();
        sc.AddSingleton<LiveWorkoutWatcher>();   // the nudge, the return popup and the 4-hour stop (18b)
        sc.AddSingleton<AwayWatcher>();   // notices a trip away from the home city (0.19.0)
        sc.AddTransient<NearbyCityFinder>();   // "Show me gyms near me" on vacation (0.19.0)
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
        sc.AddTransient<VacationService>();
        sc.AddTransient<ExerciseService>();
        sc.AddTransient<DayExerciseService>();
        sc.AddTransient<SetService>();
        sc.AddTransient<LiveWorkoutService>();
        sc.AddTransient<CardioService>();   // the cardio timer after a workout (0.18.2)
        sc.AddTransient<WorkoutMenuService>();
        sc.AddTransient<WorkoutsViewModel>();
        sc.AddTransient<DayViewModel>();
        sc.AddTransient<ExerciseViewModel>();
        sc.AddTransient<CustomExercisesViewModel>();
        sc.AddTransient<ExerciseFormViewModel>();
        sc.AddTransient<AvatarPickerViewModel>();
        sc.AddTransient<ProfileService>();
        sc.AddTransient<CalorieService>();
        // Food log (Phase 26): the same in local and server social modes, food isn't social. One catalog and one Open Food
        // Facts client, so the extracted file and the request limits are shared
        sc.AddSingleton(_ => new FoodCatalog(platform.DataDirectory));
        sc.AddSingleton(_ => new OpenFoodFactsClient());
        sc.AddTransient<FoodService>();
        sc.AddTransient(sp => new AddFoodViewModel(sp.GetRequiredService<FoodService>(), sp.GetRequiredService<ProfileService>(),
            sp.GetRequiredService<SessionService>(), sp.GetRequiredService<IBarcodeScanner>(),
            () => sp.GetRequiredService<MainViewModel>().ClosePopup()));
        sc.AddTransient<ProfileViewModel>();
        sc.AddTransient<ProfileSetupViewModel>();
        sc.AddTransient<ConfirmEmailViewModel>();
        sc.AddTransient<ForgotPasswordViewModel>();
        sc.AddTransient<CaloriesViewModel>();
        sc.AddSingleton(_ => new GeocodingService());   // one instance, so its one-request-per-second limit holds
        sc.AddTransient<CityService>();
        // Our server's gyms first in the server-backed wiring (0.27.0); the local mode has none and asks Overpass only
        sc.AddSingleton(sp => new GymFetchService(sp.GetRequiredService<IDbContextFactory<AppDbContext>>(), sp.GetService<IServerGyms>()));
        sc.AddTransient<GymService>();
        sc.AddSingleton(sp => new GymAddressLookupService(
            sp.GetRequiredService<IDbContextFactory<AppDbContext>>(), sp.GetRequiredService<GeocodingService>()));
        sc.AddTransient<GymPickerViewModel>();
        sc.AddTransient<GymSummaryService>();
        sc.AddTransient<GymsViewModel>();
        sc.AddTransient<PointsHistoryViewModel>();
        sc.AddSingleton<FeedbackViewModel>();   // one, so a half-written note survives closing the popup
        sc.AddTransient<FriendMapService>();
        sc.AddTransient<IInboxSource, FriendRequestInboxSource>();
        sc.AddTransient<IInboxSource, ShareInboxSource>();
        sc.AddTransient<SplitShareService>();
        sc.AddTransient<ShareDialogViewModel>();
        sc.AddTransient<ReportUserDialogViewModel>();
        sc.AddSingleton(_ => new InboxStore(platform.DataDirectory));
        sc.AddSingleton(sp => new InboxService(sp.GetServices<IInboxSource>, sp.GetRequiredService<InboxStore>(),
            sp.GetRequiredService<SessionService>()));   // one, so every sidebar shows the same badge
        sc.AddTransient(sp => new InboxViewModel(sp.GetRequiredService<InboxService>(), sp.GetRequiredService<IFriendService>(),
            sp.GetRequiredService<SessionService>(), sp.GetRequiredService<Func<MainViewModel>>(),
            sp.GetRequiredService<IShareService>(), sp.GetRequiredService<SplitShareService>(), sp.GetRequiredService<WorkoutMenuService>(),
            sp.GetRequiredService<ReportUserDialogViewModel>()));
        sc.AddTransient<FriendQrViewModel>();
        sc.AddTransient<FriendsViewModel>();
        return sc;
    }
}
