using GymApp.App.ViewModels;
using GymApp.Services;

namespace GymApp.Tests;

/// <summary>The rest day's exercise picker on phones (0.18.1): chips, search, picks in order, the limit.</summary>
public class RestDayPickerViewModelTests
{
    private static readonly DateOnly Mon = new(2026, 10, 5);

    private static readonly PickableExercise[] Choices =
    [
        new(1, "Barbell row", "Back", Mon, "60 kg × 8"),
        new(2, "Hammer curl", "Biceps", Mon.AddDays(-2), "14 kg × 12"),
        new(3, "Bench press", "Chest", null, null),
        new(4, "Lat pulldown", "Back", null, null),
        new(5, "Band pull-apart", "Custom", null, null),
    ];

    private static (RestDayPickerViewModel Vm, List<IReadOnlyList<int>> Started) Make(IReadOnlyList<PickableExercise>? choices = null)
    {
        var started = new List<IReadOnlyList<int>>();
        var vm = new RestDayPickerViewModel(choices ?? Choices, ids => { started.Add(ids); return Task.CompletedTask; }, () => { });
        return (vm, started);
    }

    private static RestDayPickItem Item(RestDayPickerViewModel vm, int id) =>
        vm.Groups.SelectMany(g => g.Items).First(i => i.ExerciseId == id);

    [Fact] public void OpensOnRecent_WithTheGroupsInTheirUsualOrder_CustomLast()
    {
        var (vm, _) = Make();
        Assert.Equal(["Recent", "All", "Chest", "Back", "Biceps", "Custom"], vm.Filters.Select(f => f.Label));
        Assert.True(vm.Filters[0].IsSelected);
        Assert.Equal([1, 2], vm.Groups.Single().Items.Select(i => i.ExerciseId));
    }

    [Fact] public void NothingDoneYet_OpensOnAll()
    {
        var (vm, _) = Make(Choices.Select(c => c with { LastDate = null, LastText = null }).ToList());
        Assert.Equal("All", vm.Filters[0].Label);
        Assert.Equal(["CHEST", "BACK", "BICEPS", "CUSTOM"], vm.Groups.Select(g => g.Title));
    }

    [Fact] public void Search_LooksAcrossEveryGroup()
    {
        var (vm, _) = Make();
        vm.Search = "pull";
        Assert.Equal([4, 5], vm.Groups.SelectMany(g => g.Items).Select(i => i.ExerciseId));
        vm.Search = "zzz";
        Assert.True(vm.NoResults);
    }

    [Fact] public async Task Picks_KeepTheirOrder_ThroughFilters_AndStartSendsThem()
    {
        var (vm, started) = Make();
        Item(vm, 2).ToggleCommand.Execute(null);
        vm.SelectFilterCommand.Execute(vm.Filters.First(f => f.Label == "Back"));
        Item(vm, 4).ToggleCommand.Execute(null);
        Item(vm, 1).ToggleCommand.Execute(null);
        Item(vm, 4).ToggleCommand.Execute(null);   // unpicked again

        Assert.Equal(2, vm.PickedCount);
        Assert.Equal("Hammer curl · Barbell row", vm.PickedText);
        Assert.Equal("Start · 2 exercises", vm.StartLabel);

        await vm.StartCommand.ExecuteAsync(null);
        Assert.Equal([2, 1], Assert.Single(started));
    }

    [Fact] public async Task Start_WithNothingPicked_SaysSo()
    {
        var (vm, started) = Make();
        await vm.StartCommand.ExecuteAsync(null);
        Assert.Empty(started);
        Assert.Equal("Pick at least one exercise.", vm.Error);
    }

    [Fact] public void ThePickLimit_IsKept()
    {
        var many = Enumerable.Range(1, WorkoutService.MaxPickedExercises + 1)
            .Select(i => new PickableExercise(i, $"Exercise {i:00}", "Back", null, null)).ToList();
        var (vm, _) = Make(many);
        foreach (var i in vm.Groups.SelectMany(g => g.Items).ToList()) i.ToggleCommand.Execute(null);
        Assert.Equal(WorkoutService.MaxPickedExercises, vm.PickedCount);
        Assert.Equal($"Pick up to {WorkoutService.MaxPickedExercises} exercises.", vm.Error);
    }
}
