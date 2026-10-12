using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Services;

namespace GymApp.Desktop.ViewModels;

public partial class DayCardViewModel : ObservableObject
{
    private readonly WorkoutsViewModel _owner;
    public MenuDay Day { get; }

    public DayCardViewModel(MenuDay day, WorkoutsViewModel owner) { Day = day; _owner = owner; }

    public string Name => Day.Name;
    public string Weekdays => Day.Weekdays.Length > 0 ? Day.Weekdays : "Not scheduled";
    public bool IsToday => Day.IsToday;
    public string CountText => Day.ExerciseCount switch
    {
        0 => "No exercises yet",
        1 => "1 exercise",
        var n => $"{n} exercises"
    };

    [RelayCommand] private void Open() => _owner.Open(this);
}

public partial class WorkoutsViewModel : ViewModelBase
{
    private readonly WorkoutMenuService _menu;
    private readonly SessionService _session;
    private readonly Func<MainViewModel> _shell;
    private string _splitName = "";

    public ObservableCollection<DayCardViewModel> Days { get; } = new();

    [ObservableProperty] public partial string Subtitle { get; set; } = "";
    [ObservableProperty] public partial bool ShowEmpty { get; set; }     // true once we know there is no split
    [ObservableProperty] public partial string? Error { get; set; }

    public WorkoutsViewModel(WorkoutMenuService menu, SessionService session, Func<MainViewModel> shell)
    {
        _menu = menu; _session = session; _shell = shell;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var menu = await _menu.GetAsync(_session.CurrentUser!.Id);
            Days.Clear();
            if (menu is null) { ShowEmpty = true; Subtitle = ""; return; }

            _splitName = menu.SplitName;
            foreach (var d in menu.Days) Days.Add(new DayCardViewModel(d, this));
            ShowEmpty = false;
            Subtitle = menu.IsRestToday ? $"{menu.SplitName} · today is a rest day" : menu.SplitName;
        }
        catch (Exception)
        {
            Error = "Could not load your workouts.";
        }
    }

    internal void Open(DayCardViewModel card) => _shell().ShowDay(card.Day, _splitName);

    [RelayCommand] private void ChooseSplit() => _shell().ShowSplits();
    [RelayCommand] private void Back() => _shell().ShowHome();
}