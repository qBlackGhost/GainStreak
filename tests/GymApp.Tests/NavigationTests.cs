using GymApp.App;
using GymApp.App.ViewModels;
using GymApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GymApp.Tests;

/// <summary>The phones' back stack and tabs in <see cref="MainViewModel"/> (16d). Run in the TestBench's local mode,
/// signed in, with a preset split chosen.</summary>
[Collection(DataDirectoryCollection.Name)]
public sealed class NavigationTests : IAsyncLifetime
{
    private readonly TempDataDirectory _data = new();
    private ServiceProvider _sp = null!;
    private MainViewModel _shell = null!;
    private WorkoutMenu _menu = null!;

    public async Task InitializeAsync()
    {
        _sp = AppServices.Create(localSocial: true, new FakePlatform()).BuildServiceProvider();
        Assert.Null(await AppStartup.PrepareAsync(_sp, localSocial: true));
        Assert.True((await _sp.GetRequiredService<IAccountService>().RegisterAsync("alice", "alice@example.com", "correct horse 1")).Success);
        var user = _sp.GetRequiredService<SessionService>().CurrentUser!;
        var splits = _sp.GetRequiredService<SplitService>();
        var preset = (await splits.GetPresetsAsync())[0];
        Assert.True(await splits.SelectAsync(user.Id, preset.Id, preset.Variants.FirstOrDefault()?.Id, null));
        _menu = (await _sp.GetRequiredService<WorkoutMenuService>().GetAsync(user.Id))!;
        _shell = _sp.GetRequiredService<MainViewModel>();
        _shell.ShowHome();
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        _data.Dispose();
    }

    private async Task<Data.Models.WorkoutSplit> APresetAsync() => (await _sp.GetRequiredService<SplitService>().GetPresetsAsync())[0];

    [Fact] public async Task Push_ThenBack_GoesThroughTheScreensInReverse()
    {
        _shell.ShowSplits();
        _shell.ShowSplitDetail(await APresetAsync());
        _shell.ShowSplitEditor(await APresetAsync());
        Assert.IsType<SplitEditorViewModel>(_shell.Current);
        Assert.True(_shell.CanGoBack);

        Assert.True(_shell.GoBack());
        Assert.IsType<SplitDetailViewModel>(_shell.Current);
        Assert.True(_shell.GoBack());
        Assert.IsType<SplitsViewModel>(_shell.Current);
        Assert.False(_shell.CanGoBack);
    }

    [Fact] public void BackAtATabRoot_ReturnsFalse_AndStays()
    {
        Assert.IsType<HomeViewModel>(_shell.Current);
        Assert.False(_shell.CanGoBack);
        Assert.False(_shell.GoBack());
        Assert.IsType<HomeViewModel>(_shell.Current);
    }

    [Fact] public void GoingBack_OpensTheScreenAgain()
    {
        var home = _shell.Current;
        _shell.ShowProfile();
        Assert.True(_shell.GoBack());
        Assert.IsType<HomeViewModel>(_shell.Current);
        Assert.NotSame(home, _shell.Current);   // fresh, so it shows what changed meanwhile
    }

    [Fact] public async Task ATab_ClearsTheStack()
    {
        _shell.ShowSplits();
        _shell.ShowSplitDetail(await APresetAsync());
        Assert.True(_shell.CanGoBack);

        _shell.ShowWorkouts();

        Assert.False(_shell.CanGoBack);
        Assert.Equal(AppTab.Workouts, _shell.SelectedTab);
        Assert.False(_shell.GoBack());
    }

    [Fact] public void OpeningAScreenAlreadyInTheStack_GoesBackToIt()
    {
        // Workouts → Day → Custom exercises, whose own back button opens the day again
        var day = _menu.Days[0];
        _shell.ShowWorkouts();
        _shell.ShowDay(day, _menu.SplitName);
        _shell.ShowCustomExercises(day, _menu.SplitName);
        _shell.ShowDay(day, _menu.SplitName);

        Assert.IsType<DayViewModel>(_shell.Current);
        Assert.True(_shell.GoBack());
        Assert.IsType<WorkoutsViewModel>(_shell.Current);
        Assert.False(_shell.CanGoBack);
    }

    [Fact] public void OpeningTheSameScreenAgain_ReplacesIt()
    {
        _shell.ShowWorkouts();
        _shell.ShowDay(_menu.Days[0], _menu.SplitName);
        _shell.ShowDay(_menu.Days[^1], _menu.SplitName);

        Assert.True(_shell.GoBack());
        Assert.IsType<WorkoutsViewModel>(_shell.Current);
    }

    [Fact] public void Feedback_ClosesFirst()
    {
        _shell.ShowProfile();
        _shell.ShowFeedback();

        Assert.True(_shell.GoBack());
        Assert.False(_shell.IsFeedbackOpen);
        Assert.IsType<ProfileViewModel>(_shell.Current);
    }

    [Fact] public void ATab_ClosesFeedback()
    {
        _shell.ShowProfile();
        _shell.ShowFeedback();

        _shell.ShowCalories();   // the tab bar stays tappable under the feedback popup on phones

        Assert.False(_shell.IsFeedbackOpen);
        Assert.IsType<CaloriesViewModel>(_shell.Current);
    }

    [Fact] public void AScreensPopup_ClosesFirst()
    {
        var home = Assert.IsType<HomeViewModel>(_shell.Current);
        home.ShowPointsHistoryCommand.Execute(null);
        Assert.True(home.IsPointsHistoryOpen);

        Assert.True(_shell.GoBack());   // at a tab root, but a popup was open
        Assert.False(home.IsPointsHistoryOpen);
        Assert.Same(home, _shell.Current);
        Assert.False(_shell.GoBack());
    }

    [Fact] public void AScreen_LightsItsOwnTab()
    {
        _shell.ShowDay(_menu.Days[0], _menu.SplitName);   // Home's "today's workout"
        Assert.Equal(AppTab.Workouts, _shell.SelectedTab);
        Assert.True(_shell.GoBack());
        Assert.Equal(AppTab.Home, _shell.SelectedTab);

        _shell.ShowInbox();   // the app bar's: no tab lit, the bars still show
        Assert.Null(_shell.SelectedTab);
        Assert.True(_shell.IsSignedInScreen);
    }

    [Fact] public void TheFriendsTab_SwitchesBetweenLeaderboardAndMap()
    {
        _shell.ShowFriends();
        _shell.ShowGyms();
        Assert.Equal(AppTab.Friends, _shell.SelectedTab);
        Assert.True(_shell.GoBack());
        Assert.IsType<FriendsViewModel>(_shell.Current);
    }

    public static TheoryData<string, AppTab?> Screens => new()
    {
        { nameof(LoginViewModel), null },
        { nameof(RegisterViewModel), null },
        { nameof(ProfileSetupViewModel), null },
        { nameof(SplitsViewModel), AppTab.Splits },
        { nameof(SplitDetailViewModel), AppTab.Splits },
        { nameof(SplitEditorViewModel), AppTab.Splits },
        { nameof(WorkoutsViewModel), AppTab.Workouts },
        { nameof(DayViewModel), AppTab.Workouts },
        { nameof(ExerciseViewModel), AppTab.Workouts },
        { nameof(CustomExercisesViewModel), AppTab.Workouts },
        { nameof(HomeViewModel), AppTab.Home },
        { nameof(CaloriesViewModel), AppTab.Calories },
        { nameof(FriendsViewModel), AppTab.Friends },
        { nameof(GymsViewModel), AppTab.Friends },
        { nameof(ProfileViewModel), null },
        { nameof(InboxViewModel), null },
    };

    [Theory, MemberData(nameof(Screens))]
    public void EveryScreen_HasItsTab(string screen, AppTab? tab) =>
        Assert.Equal(tab, MainViewModel.TabOf(typeof(MainViewModel).Assembly.GetType($"GymApp.App.ViewModels.{screen}")!));

    [Fact] public void EveryScreenTheShellShows_IsInTheTable()
    {
        // Each screen is a view model the ViewLocator pairs with a view, opened by one of MainViewModel's Show methods
        var shown = typeof(MainViewModel).GetMethods()
            .Where(m => m.Name.StartsWith("Show") && m.DeclaringType == typeof(MainViewModel) && m.Name != nameof(MainViewModel.ShowFeedback))
            .Select(m => m.Name["Show".Length..])
            .ToList();
        var table = Screens.Select(row => (string)row[0]!).ToList();
        foreach (var name in shown) Assert.Contains($"{name}ViewModel", table);
    }

    [Fact] public void TheBars_ShowOnPhones_AfterSignIn()
    {
        Assert.False(_shell.ShowsBars);   // not a phone yet
        _shell.IsPhone = true;
        Assert.True(_shell.ShowsBars);

        _shell.ShowProfile();
        Assert.True(_shell.ShowsBars);

        _shell.ShowLogin();
        Assert.Null(_shell.SelectedTab);
        Assert.False(_shell.ShowsBars);
    }
}
