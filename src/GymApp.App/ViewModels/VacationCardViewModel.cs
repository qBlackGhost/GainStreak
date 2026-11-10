using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Core;
using GymApp.Services;

namespace GymApp.App.ViewModels;

/// <summary>
/// Vacation mode (0.19.0) for one screen: where it stands, "Going away?" (<see cref="Sheet"/>) and "I'm back" with its
/// confirmation. Profile → Vacation shows all of it; Home shows I'm back on the streak card and (19c) the away prompt.
/// <paramref name="changed"/> runs after a vacation starts or ends, so the screen and the app bar catch up.
/// </summary>
public partial class VacationCardViewModel(VacationService vacations, Func<int?> userId, Func<Task> changed) : ObservableObject
{
    public const string Blurb = "Freeze your streak for up to 7 days, once every 2 months.";

    private VacationPeriod? _current;

    [ObservableProperty] public partial string StateText { get; set; } = "";
    [ObservableProperty] public partial string? RefusalText { get; set; }
    [ObservableProperty] public partial bool CanPlan { get; set; }
    [ObservableProperty] public partial bool HasVacation { get; set; }

    /// <summary>I'm back (or Cancel vacation) has something to do: not on the last vacation day, nor after ending it.</summary>
    [ObservableProperty] public partial bool CanEnd { get; set; }
    [ObservableProperty] public partial string EndLabel { get; set; } = "I'm back";

    /// <summary>"Going away?" while it's open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSheetOpen))]
    public partial VacationSheetViewModel? Sheet { get; set; }

    public bool IsSheetOpen => Sheet is not null;

    [ObservableProperty] public partial bool IsConfirmingEnd { get; set; }
    [ObservableProperty] public partial string EndTitle { get; set; } = "";
    [ObservableProperty] public partial string EndText { get; set; } = "";
    [ObservableProperty] public partial string EndButton { get; set; } = "";
    [ObservableProperty] public partial string? Error { get; set; }

    /// <summary>What just happened, for a note on the screen: "Vacation ended…" or "Vacation cancelled."; null after
    /// a start.</summary>
    public string? LastChange { get; private set; }

    public async Task LoadAsync()
    {
        if (userId() is not { } id) return;
        var today = AppClock.Today;
        var state = await vacations.GetStateAsync(id, today);
        _current = state.Current;
        HasVacation = _current is not null;
        CanPlan = state.Check.Allowed;
        var notYet = _current is { } c && c.Start > today;
        CanEnd = _current is { } cur && (notYet || VacationRules.BackOn(cur) > today.AddDays(1));
        EndLabel = notYet ? "Cancel vacation" : "I'm back";
        RefusalText = state.Check.Refusal == VacationRefusal.SettleMissedDay ? VacationService.RefusalText(state.Check) : null;
        StateText = _current is { } v
            ? (notYet ? "Starts tomorrow · back on " : "On vacation · back on ") + VacationSheetViewModel.Short(VacationRules.BackOn(v))
            : state.Check.Refusal == VacationRefusal.Cooldown && state.Check.AvailableFrom is { } next
                ? "Next available on " + VacationSheetViewModel.Short(next)
            : "Available";
    }

    [RelayCommand]
    private async Task PlanAsync()
    {
        if (userId() is not { } id || !CanPlan) return;
        var sheet = new VacationSheetViewModel(vacations, id, StartedAsync, () => Sheet = null);
        await sheet.LoadAsync();
        Sheet = sheet;
    }

    private async Task StartedAsync(VacationResult result)
    {
        Sheet = null;
        LastChange = null;
        await LoadAsync();
        await changed();
    }

    [RelayCommand]
    private void AskEnd()
    {
        if (_current is not { } v || !CanEnd) return;
        var notYet = v.Start > AppClock.Today;
        EndTitle = notYet ? "Cancel your vacation?" : "End your vacation?";
        EndText = notYet
            ? "It hasn't started yet, so you can plan another one any time."
            : "Today stays frozen, and your streak counts again from tomorrow. Your next vacation is available on "
              + VacationSheetViewModel.Short(v.Start.AddDays(VacationRules.CooldownDays)) + ".";
        EndButton = notYet ? "Cancel vacation" : "End vacation";
        Error = null;
        IsConfirmingEnd = true;
    }

    [RelayCommand] private void KeepVacation() => IsConfirmingEnd = false;

    [RelayCommand]
    private async Task EndAsync()
    {
        if (userId() is not { } id) return;
        try
        {
            var notYet = _current is { } v && v.Start > AppClock.Today;
            var r = await vacations.EndEarlyAsync(id);
            if (!r.Success) { Error = r.Message; return; }
            LastChange = notYet ? "Vacation cancelled." : "Vacation ended. Your streak counts again from tomorrow.";
            IsConfirmingEnd = false;
            await LoadAsync();
            await changed();
        }
        catch (Exception) { Error = "Couldn't end your vacation. Try again."; }
    }

    /// <summary>Back closes the sheet or the question first.</summary>
    public bool CloseOnBack()
    {
        if (Sheet is not null) { Sheet = null; return true; }
        if (IsConfirmingEnd) { IsConfirmingEnd = false; return true; }
        return false;
    }
}
