using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Data.Models;
using GymApp.Services;

namespace GymApp.App.ViewModels;

public partial class DayCellViewModel(DayOfWeek weekday) : ObservableObject
{
    public DayOfWeek Weekday { get; } = weekday;
    public string Label { get; } = weekday.ToString()[..3];   // "Mon", "Tue", ...

    [ObservableProperty] public partial bool IsSelected { get; set; }
    [ObservableProperty] public partial string Name { get; set; } = "";
}

public partial class SplitEditorViewModel : ViewModelBase
{
    private readonly SplitService _splits;
    private readonly SessionService _session;
    private readonly Func<MainViewModel> _shell;
    private int? _splitId;

    public IReadOnlyList<DayCellViewModel> Days { get; } =
        new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
                DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday }
            .Select(d => new DayCellViewModel(d)).ToList();

    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial string? Error { get; set; }
    [ObservableProperty] public partial string? NameError { get; set; }
    [ObservableProperty] public partial string? DaysError { get; set; }

    public string Title => _splitId is null ? "New split" : "Edit split";
    public string SaveLabel => _splitId is null ? "Create split" : "Save changes";

    public string DaysSummary
    {
        get
        {
            var training = Days.Count(d => d.IsSelected);
            return $"{training} training {(training == 1 ? "day" : "days")} · {Days.Count - training} rest";
        }
    }

    public SplitEditorViewModel(SplitService splits, SessionService session, Func<MainViewModel> shell)
    {
        _splits = splits; _session = session; _shell = shell;
        foreach (var d in Days)
            d.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DayCellViewModel.IsSelected))
                    OnPropertyChanged(nameof(DaysSummary));
            };
    }

    /// <summary>Call right after resolving; null = creating a new split.</summary>
    public void Load(WorkoutSplit? split)
    {
        _splitId = split?.Id;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(SaveLabel));
        Name = split?.Name ?? "";
        if (split is null) return;

        foreach (var day in split.Days.Where(d => d.Weekday is not null))
        {
            var cell = Days.First(c => c.Weekday == day.Weekday);
            cell.IsSelected = true;
            cell.Name = day.Name;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var days = Days.Where(d => d.IsSelected).Select(d => new DayInput(d.Weekday, d.Name)).ToList();
        var userId = _session.CurrentUser!.Id;

        Error = NameError = DaysError = null;
        try
        {
            var result = _splitId is null
                ? await _splits.CreateAsync(userId, Name, days)
                : await _splits.UpdateAsync(userId, _splitId.Value, Name, days);

            if (result.Success) { _shell().ShowSplits(); return; }
            switch (result.Field)
            {
                case FormField.Name: NameError = result.Error; break;
                case FormField.Days: DaysError = result.Error; break;
                default: Error = result.Error; break;
            }
        }
        catch (Exception)
        {
            Error = "Could not save your split.";
        }
    }

    [RelayCommand] private void Cancel() => _shell().ClosePopup();
}