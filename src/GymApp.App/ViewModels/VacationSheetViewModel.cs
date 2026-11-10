using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Core;
using GymApp.Services;

namespace GymApp.App.ViewModels;

/// <summary>One return date on the vacation sheet: the weekday over the day of the month.</summary>
public partial class VacationDayViewModel(DateOnly date, Action<VacationDayViewModel> pick) : ObservableObject
{
    public DateOnly Date { get; } = date;
    public string Weekday { get; } = date.ToString("ddd", CultureInfo.InvariantCulture);
    public string Number { get; } = date.Day.ToString(CultureInfo.InvariantCulture);
    public string Label { get; } = "Back on " + date.ToString("dddd d MMMM", CultureInfo.InvariantCulture);

    [ObservableProperty] public partial bool IsSelected { get; set; }

    /// <summary>A frozen day before the picked return date.</summary>
    [ObservableProperty] public partial bool IsInRange { get; set; }

    [RelayCommand] private void Pick() => pick(this);
}

/// <summary>
/// "Going away?" (0.19.0, the GainStreak Mobile canvas's Home on vacation): the return date, at most 7 days after the
/// start, and Start vacation. Profile → Vacation shows it, and in 19c the away prompt on Home. The vacation starts
/// today, or tomorrow when today's workout is already logged (<see cref="VacationRules.StartDate"/>).
/// </summary>
public partial class VacationSheetViewModel : ViewModelBase
{
    public const int DefaultDays = 3;

    private readonly VacationService _vacations;
    private readonly int _userId;
    private readonly Func<VacationResult, Task> _started;
    private readonly Action _cancelled;
    private DateOnly _start;

    public VacationSheetViewModel(VacationService vacations, int userId, Func<VacationResult, Task> started, Action cancelled)
    {
        _vacations = vacations;
        _userId = userId;
        _started = started;
        _cancelled = cancelled;
    }

    [ObservableProperty] public partial IReadOnlyList<VacationDayViewModel> Days { get; set; } = [];
    [ObservableProperty] public partial string FrozenRange { get; set; } = "";
    [ObservableProperty] public partial string FrozenDetail { get; set; } = "";
    [ObservableProperty] public partial string NextAvailable { get; set; } = "";
    [ObservableProperty] public partial string? StartsTomorrowNote { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }

    public async Task LoadAsync()
    {
        var today = AppClock.Today;
        _start = await _vacations.StartDateAsync(_userId, today);
        var (first, last) = VacationRules.ReturnDateRange(_start);
        Days = Enumerable.Range(0, last.DayNumber - first.DayNumber + 1)
            .Select(i => new VacationDayViewModel(first.AddDays(i), Select)).ToList();
        StartsTomorrowNote = _start > today ? "Today's workout is logged, so your vacation starts tomorrow." : null;
        NextAvailable = "One vacation every 2 months. Your next one would be available on "
                        + Short(_start.AddDays(VacationRules.CooldownDays)) + ".";
        Select(Days[Math.Min(DefaultDays, Days.Count) - 1]);
    }

    public DateOnly? ReturnDate => Days.FirstOrDefault(d => d.IsSelected)?.Date;

    private void Select(VacationDayViewModel day)
    {
        foreach (var d in Days)
        {
            d.IsSelected = d == day;
            d.IsInRange = d.Date < day.Date;
        }
        var lastFrozen = day.Date.AddDays(-1);
        var n = day.Date.DayNumber - _start.DayNumber;
        FrozenRange = n == 1 ? "Frozen " + Short(_start) : $"Frozen {Short(_start)} to {Short(lastFrozen)}";
        FrozenDetail = (n == 1 ? "1 day" : $"{n} days") + " · back on "
                       + day.Date.ToString("dddd d MMMM", CultureInfo.InvariantCulture);
        Error = null;
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (ReturnDate is not { } back || IsBusy) return;
        IsBusy = true;
        try
        {
            var r = await _vacations.StartAsync(_userId, back);
            if (!r.Success) { Error = r.Message; return; }
            await _started(r);
        }
        catch (Exception) { Error = "Couldn't start your vacation. Try again."; }
        finally { IsBusy = false; }
    }

    [RelayCommand] private void Cancel() => _cancelled();

    public static string Short(DateOnly d) => d.ToString("ddd d MMM", CultureInfo.InvariantCulture);
}
