using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Core;
using GymApp.Services;

namespace GymApp.Desktop.ViewModels;

/// <summary>One option on the missed-day card. Choosing it only asks for confirmation; the card stores it.</summary>
public partial class MissedDayOptionViewModel(MissedDayViewModel owner, MissedDayOption option, string tag) : ObservableObject
{
    public MissedDayOption Option { get; } = option;
    public string Title => Option.Title;
    public string Preview => Option.Preview;

    /// <summary>"1 left this week", or "Streak resets" for skipping.</summary>
    public string Tag { get; } = tag;
    public bool IsSkip => Option.Choice == MissChoice.Continue;

    /// <summary>The option waiting for confirmation.</summary>
    [ObservableProperty] public partial bool IsSelected { get; set; }

    /// <summary>The new week after a whole-schedule shift, e.g. "Wed Pull", "Fri Rest".</summary>
    public IReadOnlyList<string> NewWeek { get; } =
        option.NewWeek?.Select(d => $"{d.Weekday.ToString()[..3]}\n{d.Title}").ToList() ?? [];
    public bool HasNewWeek => NewWeek.Count > 0;

    [RelayCommand] private void Choose() => owner.AskToConfirm(this);
}

/// <summary>
/// The card Home shows the day after a missed training day, while a streak is at stake: the three options (or the
/// ones still available this week), each asking for confirmation. The choice is final.
/// </summary>
public partial class MissedDayViewModel : ObservableObject
{
    private readonly MissedDayService _service;
    private readonly int _userId;
    private readonly Func<MissedDayResult, Task> _chosen;

    public MissedDayViewModel(MissedDayService service, int userId, MissedDayPrompt prompt, Func<MissedDayResult, Task> chosen)
    {
        _service = service;
        _userId = userId;
        _chosen = chosen;
        Heading = $"You missed {prompt.MissedDayName} yesterday.";
        Subheading = $"Your {prompt.StreakAtStake}-day streak is at stake. What do you want to do?";
        Options = prompt.Options.Select(o => new MissedDayOptionViewModel(this, o, o.Choice switch
        {
            MissChoice.ShiftWeek => $"{prompt.ShiftWeeksLeft} left this week",
            MissChoice.ShiftToRest => $"{prompt.MakeUpsLeft} left this week",
            _ => "Streak resets"
        })).ToList();
        Notes = prompt.Notes;
    }

    public string Heading { get; }
    public string Subheading { get; }
    public IReadOnlyList<MissedDayOptionViewModel> Options { get; }
    public IReadOnlyList<string> Notes { get; }
    public bool HasNotes => Notes.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirming))]
    [NotifyPropertyChangedFor(nameof(ConfirmText))]
    public partial MissedDayOptionViewModel? Pending { get; set; }

    public bool IsConfirming => Pending is not null;

    partial void OnPendingChanged(MissedDayOptionViewModel? value)
    {
        foreach (var o in Options) o.IsSelected = o == value;
    }
    public string ConfirmText => Pending?.Option.ConfirmText ?? "";

    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }

    internal void AskToConfirm(MissedDayOptionViewModel option)
    {
        Error = null;
        Pending = option;
    }

    [RelayCommand] private void Cancel() => Pending = null;

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (Pending is not { } option || IsBusy) return;
        IsBusy = true;
        try
        {
            var result = await _service.ChooseAsync(_userId, option.Option.Choice);
            if (!result.Success) { Error = result.Error; Pending = null; return; }
            await _chosen(result);
        }
        catch (Exception)
        {
            Error = "Could not save your choice.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
