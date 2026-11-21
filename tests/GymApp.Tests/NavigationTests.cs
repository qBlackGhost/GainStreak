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
        Assert.IsType<SplitDetailViewModel>(_shell.Current);
        Assert.True(_shell.CanGoBack);

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

    [Fact] public async Task ThePopup_OpensOverTheScreen_AndBackClosesItFirst()
    {
        _shell.ShowSplits();
        _shell.ShowSplitDetail(await APresetAsync());
        _shell.OpenSplitEditor(await APresetAsync());
        Assert.IsType<SplitEditorViewModel>(_shell.Popup);
        Assert.IsType<SplitDetailViewModel>(_shell.Current);

        Assert.True(_shell.GoBack());
        Assert.Null(_shell.Popup);
        Assert.IsType<SplitDetailViewModel>(_shell.Current);
    }

    [Fact] public async Task OpeningAScreen_ClosesThePopup()
    {
        _shell.ShowSplits();
        _shell.OpenSplitEditor(await APresetAsync());
        _shell.ShowHome();
        Assert.Null(_shell.Popup);
    }

    [Fact] public async Task TheExerciseForm_NeedsAGroup_ThenSavesAndCloses()
    {
        Data.Models.Exercise? saved = null;
        _shell.OpenExerciseForm(null, "", e => { saved = e; return Task.CompletedTask; });
        var form = Assert.IsType<ExerciseFormViewModel>(_shell.Popup);
        form.Name = "Landmine press";

        await form.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(form.GroupError);
        Assert.Same(form, _shell.Popup);

        form.SelectGroupCommand.Execute(form.Groups[2]);
        await form.SaveCommand.ExecuteAsync(null);
        Assert.Null(_shell.Popup);
        Assert.Equal("Landmine press", saved?.Name);
        Assert.Equal(form.Groups[2].Group, saved?.MuscleGroup);
    }

    [Fact] public async Task NewExerciseOnADay_AddsItAndOpensIt()
    {
        var day = _menu.Days[0];
        _shell.ShowDay(day, _menu.SplitName);
        var dayVm = Assert.IsType<DayViewModel>(_shell.Current);
        dayVm.NewExerciseCommand.Execute(null);
        var form = Assert.IsType<ExerciseFormViewModel>(_shell.Popup);
        Assert.True(form.CanManage);
        form.Name = "Landmine press";
        form.SelectGroupCommand.Execute(form.Groups[0]);

        await form.SaveCommand.ExecuteAsync(null);

        Assert.Null(_shell.Popup);
        Assert.IsType<ExerciseViewModel>(_shell.Current);
    }

    private async Task<CustomExercisesViewModel> MyExercisesAsync(params string[] names)
    {
        var userId = _sp.GetRequiredService<SessionService>().CurrentUser!.Id;
        foreach (var name in names)
            Assert.True((await _sp.GetRequiredService<ExerciseService>().CreateAsync(userId, name, Data.Models.MuscleGroup.Back)).Success);
        var vm = _sp.GetRequiredService<CustomExercisesViewModel>();
        await vm.ReloadAsync();
        return vm;
    }

    [Fact] public async Task MyExercises_GroupByMuscle_AndSearchFilters()
    {
        var vm = await MyExercisesAsync("Seal row", "Pendlay row", "Rack pull");
        Assert.Equal("BACK", Assert.Single(vm.Groups).Title);
        Assert.Equal(3, vm.Groups[0].Items.Count);
        Assert.Equal("Not in a day yet", vm.Groups[0].Items[0].UsageText);

        vm.Search = "ROW";
        Assert.Equal(2, vm.Groups[0].Items.Count);

        vm.Search = "nothing";
        Assert.Empty(vm.Groups);
        Assert.True(vm.NoMatches);
    }

    [Fact] public async Task MyExercises_DeleteAsksFirst_AndBackKeepsIt()
    {
        var vm = await MyExercisesAsync("Seal row");
        await vm.Groups[0].Items[0].DeleteCommand.ExecuteAsync(null);
        Assert.True(vm.IsConfirmingDelete);
        Assert.Equal("Delete Seal row?", vm.DeleteTitle);

        Assert.True(vm.CloseOnBack());
        Assert.False(vm.IsConfirmingDelete);
        Assert.False(vm.IsEmpty);

        await vm.Groups[0].Items[0].DeleteCommand.ExecuteAsync(null);
        await vm.ConfirmDeleteCommand.ExecuteAsync(null);
        Assert.False(vm.IsConfirmingDelete);
        Assert.True(vm.IsEmpty);
    }

    [Fact] public async Task TheAvatarPicker_SavesAndUpdatesTheAppBar()
    {
        _shell.OpenAvatarPicker(null, "AL", 3, () => Task.CompletedTask);
        var picker = Assert.IsType<AvatarPickerViewModel>(_shell.Popup);
        Assert.Equal("black", picker.Preview);

        picker.ShowIconCommand.Execute(null);
        Assert.Equal("black:dumbbell", picker.Preview);
        picker.SelectColorCommand.Execute(picker.Colors.First(c => c.Color == "teal"));
        picker.SelectIconCommand.Execute(picker.Icons.First(i => i.Icon == "flame"));
        Assert.Equal("teal:flame", picker.Preview);

        await picker.SaveCommand.ExecuteAsync(null);
        await _shell.RefreshAvatarAsync();
        Assert.Null(_shell.Popup);
        Assert.Equal("teal:flame", _shell.OwnAvatar);
    }

    [Fact] public void TheAvatarPicker_EveryColourStaysOnTheIconTab()
    {
        _shell.OpenAvatarPicker("teal:flame", "AL", 1, () => Task.CompletedTask);
        var picker = Assert.IsType<AvatarPickerViewModel>(_shell.Popup);
        Assert.True(picker.UsesIcon);

        foreach (var color in picker.Colors)
        {
            picker.SelectColorCommand.Execute(color);
            Assert.True(picker.UsesIcon);
            Assert.Equal($"{color.Color}:flame", picker.Preview);
        }
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
        { nameof(ConfirmEmailViewModel), null },
        { nameof(ForgotPasswordViewModel), null },
        { nameof(SplitsViewModel), AppTab.Splits },
        { nameof(SplitDetailViewModel), AppTab.Splits },
        { nameof(WorkoutsViewModel), AppTab.Workouts },
        { nameof(DayViewModel), AppTab.Workouts },
        { nameof(ExerciseViewModel), AppTab.Workouts },
        { nameof(CustomExercisesViewModel), AppTab.Workouts },
        { nameof(HomeViewModel), AppTab.Home },
        { nameof(CaloriesViewModel), AppTab.Calories },
        { nameof(FriendsViewModel), AppTab.Friends },
        { nameof(GymsViewModel), AppTab.Friends },
        { nameof(ProfileViewModel), null },
        { nameof(BadgesViewModel), null },
        { nameof(FriendBadgesViewModel), AppTab.Friends },
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
