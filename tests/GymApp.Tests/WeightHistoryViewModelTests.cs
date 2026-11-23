using GymApp.App.ViewModels;
using GymApp.Core;
using GymApp.Data.Models;

namespace GymApp.Tests;

public class WeightHistoryViewModelTests
{
    private static WeightLog Log(int id, int month, int day, double kg) => new() { Id = id, Date = new DateOnly(2026, month, day), WeightKg = kg };

    private static List<WeightLog> Logs() => [Log(1, 10, 28, 81.2), Log(2, 11, 2, 80.8), Log(3, 11, 20, 80.8), Log(4, 11, 23, 80.1)];

    [Fact] public void ListsEveryWeighIn_ByMonth_NewestFirst_WithTheChangeSinceTheOneBefore()
    {
        var vm = new WeightHistoryViewModel(Logs(), UnitSystem.Metric, _ => Task.FromResult<IReadOnlyList<WeightLog>?>(null), () => { });
        Assert.Equal("4 weigh-ins · since 28 Oct 2026", vm.Summary);
        Assert.Equal(["NOVEMBER 2026", "OCTOBER 2026"], vm.Groups.Select(g => g.Title));
        var nov = vm.Groups[0].Rows;
        Assert.Equal(["Mon 23 Nov", "Fri 20 Nov", "Mon 2 Nov"], nov.Select(r => r.DateText));
        Assert.Equal(["−0.7", "±0", "−0.4"], nov.Select(r => r.ChangeText));
        Assert.Equal("80.1 kg", nov[0].WeightText);
        Assert.Equal("", vm.Groups[1].Rows[0].ChangeText);   // the first weigh-in has nothing before it
    }

    [Fact] public async Task Delete_ShowsWhatIsLeft_OrSaysItFailed()
    {
        var left = Logs().Where(l => l.Id != 4).ToList();
        var deleted = new List<int>();
        var vm = new WeightHistoryViewModel(Logs(), UnitSystem.Imperial, e => { deleted.Add(e.Id); return Task.FromResult<IReadOnlyList<WeightLog>?>(left); }, () => { });
        Assert.EndsWith("lb", vm.Groups[0].Rows[0].WeightText);

        await vm.Groups[0].Rows[0].DeleteCommand.ExecuteAsync(null);
        Assert.Equal([4], deleted);
        Assert.Equal("3 weigh-ins · since 28 Oct 2026", vm.Summary);

        var failing = new WeightHistoryViewModel(Logs(), UnitSystem.Metric, _ => Task.FromResult<IReadOnlyList<WeightLog>?>(null), () => { });
        await failing.Groups[0].Rows[0].DeleteCommand.ExecuteAsync(null);
        Assert.Equal("Could not delete that weigh-in.", failing.Error);
        Assert.Equal(4, failing.Groups.Sum(g => g.Rows.Count));
    }
}
