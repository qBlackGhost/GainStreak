using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Core;
using GymApp.Data.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace GymApp.App.ViewModels;

/// <summary>One weigh-in in the History sheet: the day, the change since the weigh-in before it, the weight, and Delete.</summary>
public partial class WeightHistoryRowViewModel(WeightLog entry, string dateText, string weightText, string changeText,
    Func<WeightLog, Task> delete) : ObservableObject
{
    public WeightLog Entry { get; } = entry;
    public string DateText { get; } = dateText;
    public string WeightText { get; } = weightText;
    public string ChangeText { get; } = changeText;
    [RelayCommand] private Task Delete() => delete(Entry);
}

/// <summary>A month of weigh-ins: "NOVEMBER 2026" and its rows, newest first.</summary>
public record WeightHistoryGroup(string Title, IReadOnlyList<WeightHistoryRowViewModel> Rows);

/// <summary>
/// The phones' weight History sheet (Phase 27): every weigh-in, newest first, by month, each with the change since the one before
/// and a Delete button. Deleting reloads the Profile's chart and the list.
/// </summary>
public partial class WeightHistoryViewModel : ViewModelBase
{
    private readonly Func<WeightLog, Task<IReadOnlyList<WeightLog>?>> _delete;
    private readonly Action _close;
    private readonly UnitSystem _units;

    public WeightHistoryViewModel(IReadOnlyList<WeightLog> weighIns, UnitSystem units,
        Func<WeightLog, Task<IReadOnlyList<WeightLog>?>> delete, Action close)
    {
        _units = units;
        _delete = delete;
        _close = close;
        Apply(weighIns);
    }

    [ObservableProperty] public partial IReadOnlyList<WeightHistoryGroup> Groups { get; set; } = [];
    [ObservableProperty] public partial string Summary { get; set; } = "";
    [ObservableProperty] public partial bool IsEmpty { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }

    private void Apply(IReadOnlyList<WeightLog> weighIns)
    {
        var newest = weighIns.OrderByDescending(w => w.Date).ToList();
        IsEmpty = newest.Count == 0;
        Summary = IsEmpty ? "No weigh-ins yet."
            : $"{newest.Count} weigh-in{(newest.Count == 1 ? "" : "s")} · since {newest[^1].Date.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}";

        double Shown(double kg) => _units == UnitSystem.Metric ? kg : Units.KgToLb(kg);
        var rows = newest.Select((w, i) =>
        {
            var change = i + 1 < newest.Count ? Math.Round(Shown(w.WeightKg) - Shown(newest[i + 1].WeightKg), 1) : (double?)null;
            var changeText = change switch
            {
                null => "",
                0 => "±0",
                > 0 => "+" + change.Value.ToString("0.0", CultureInfo.InvariantCulture),
                _ => "−" + Math.Abs(change.Value).ToString("0.0", CultureInfo.InvariantCulture),
            };
            return new WeightHistoryRowViewModel(w, w.Date.ToString("ddd d MMM", CultureInfo.InvariantCulture),
                Units.FormatWeight(w.WeightKg, _units), changeText, DeleteAsync);
        }).ToList();
        Groups = rows.GroupBy(r => (r.Entry.Date.Year, r.Entry.Date.Month))
            .Select(g => new WeightHistoryGroup(
                new DateOnly(g.Key.Year, g.Key.Month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant(), g.ToList()))
            .ToList();
    }

    private async Task DeleteAsync(WeightLog entry)
    {
        Error = null;
        var left = await _delete(entry);
        if (left is null) { Error = "Could not delete that weigh-in."; return; }
        Apply(left);
    }

    [RelayCommand] private void Close() => _close();
}
