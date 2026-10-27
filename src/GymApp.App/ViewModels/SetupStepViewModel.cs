using System;
using CommunityToolkit.Mvvm.Input;

namespace GymApp.App.ViewModels;

/// <summary>
/// One step of the dashboard's "Get set up" card (design: FirstTime board). A step that's done shows its result
/// ("Push / Pull / Legs · today: Push"); the first step still to do is highlighted; a locked step waits for an earlier one.
/// </summary>
public partial class SetupStepViewModel(int number, string title, string text, string cta, string? result,
                                        bool isLocked, Action open)
{
    public string Number { get; } = number.ToString();
    public string Title { get; } = title;
    public string Text { get; } = text;
    public string Cta { get; } = cta;

    /// <summary>What was set up, shown once the step is done; null while it isn't.</summary>
    public string? Result { get; } = result;
    public bool IsDone => Result is not null;
    public bool IsTodo => !IsDone;
    public bool IsLocked { get; } = isLocked;

    /// <summary>The first step still to do: an accent outline and a primary button.</summary>
    public bool IsNext { get; set; }
    public bool IsOther => !IsNext;

    [RelayCommand]
    private void Open()
    {
        if (!IsLocked) open();
    }
}
