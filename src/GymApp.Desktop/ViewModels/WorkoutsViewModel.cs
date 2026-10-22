using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Services;

namespace GymApp.Desktop.ViewModels;

/// <summary>One exercise line on a day card: "Bench press", "1 warm-up + 3 working".</summary>
public record DayExerciseLine(string Name, string Sets);

/// <summary>One day of the week strip at the top of Workouts.</summary>
public class WeekdayTileViewModel(MenuWeekday day)
{
    public string Day { get; } = day.Date.ToString("ddd", CultureInfo.InvariantCulture);
    public string Name { get; } = day.Name;
    public bool IsTodayTraining { get; } = day.IsToday && day.IsTraining;
    public bool IsTodayRest { get; } = day.IsToday && !day.IsTraining;
    public bool IsRest { get; } = !day.IsToday && !day.IsTraining;
}

public partial class DayCardViewModel : ObservableObject
{
    /// <summary>Exercises listed on the card; the rest show as "+2 more".</summary>
    private const int Shown = 3;

    private readonly WorkoutsViewModel _owner;
    public MenuDay Day { get; }

    public DayCardViewModel(MenuDay day, bool showNext, WorkoutsViewModel owner)
    {
        Day = day;
        _owner = owner;
        IsNext = showNext && day.IsNext && !day.IsToday;
        Exercises = day.Exercises.Take(Shown)
            .Select(e => new DayExerciseLine(e.Name, $"{e.WarmupSets} warm-up + {e.WorkingSets} working")).ToList();
    }

    public string Name => Day.Name;
    public string Weekdays => Day.Weekdays.Length > 0 ? Day.Weekdays : "Not scheduled";
    public IReadOnlyList<string> WeekdayChips => Day.Weekdays.Length > 0 ? Day.Weekdays.Split(" · ") : ["Not scheduled"];
    public bool IsToday => Day.IsToday;
    public bool IsNext { get; }
    public IReadOnlyList<DayExerciseLine> Exercises { get; }
    public bool HasExercises => Day.ExerciseCount > 0;
    public bool IsEmpty => Day.ExerciseCount == 0;
    public string? MoreText => Day.ExerciseCount > Shown ? $"+{Day.ExerciseCount - Shown} more" : null;

    /// <summary>"3 exercises · 10 sets" (warm-ups included).</summary>
    public string CountText
    {
        get
        {
            var n = Day.ExerciseCount;
            if (n == 0) return "0 exercises";
            var sets = Day.Exercises.Sum(e => e.WarmupSets + e.WorkingSets);
            return $"{n} exercise{(n == 1 ? "" : "s")} · {sets} sets";
        }
    }

    public string OpenLabel => IsEmpty ? "Add exercises" : IsToday ? "Open today" : "Open";
    public bool IsAccentOpen => IsEmpty || IsToday;

    [RelayCommand] private void Open() => _owner.Open(this);
}

public partial class WorkoutsViewModel : ViewModelBase
{
    private readonly WorkoutMenuService _menu;
    private readonly SessionService _session;
    private readonly Func<MainViewModel> _shell;
    private string _splitName = "";

    public ObservableCollection<DayCardViewModel> Days { get; } = new();

    public ObservableCollection<WeekdayTileViewModel> Week { get; } = new();

    public string DateText => DateTime.Now.ToString("dddd, d MMMM", CultureInfo.InvariantCulture);

    [ObservableProperty] public partial string Subtitle { get; set; } = "";
    [ObservableProperty] public partial string SplitName { get; set; } = "";
    [ObservableProperty] public partial string TodayNote { get; set; } = "";
    [ObservableProperty] public partial bool IsRestToday { get; set; }
    [ObservableProperty] public partial bool HasSplit { get; set; }
    [ObservableProperty] public partial string? NextName { get; set; }
    [ObservableProperty] public partial bool ShowEmpty { get; set; }     // true once we know there is no split
    [ObservableProperty] public partial string? Error { get; set; }
    [ObservableProperty] public partial string? Notice { get; set; }

    /// <summary>The popup behind the Share button.</summary>
    public ShareDialogViewModel Share { get; }

    public WorkoutsViewModel(WorkoutMenuService menu, SessionService session, Func<MainViewModel> shell, ShareDialogViewModel share)
    {
        _menu = menu; _session = session; _shell = shell;
        Share = share;
        _ = LoadAsync();
    }

    [RelayCommand] private Task OpenShare() => Share.OpenAsync(ShareMode.Workout, notice => Notice = notice);

    private async Task LoadAsync()
    {
        try
        {
            var menu = await _menu.GetAsync(_session.CurrentUser!.Id);
            Days.Clear();
            Week.Clear();
            if (menu is null) { ShowEmpty = true; HasSplit = false; Subtitle = ""; return; }

            _splitName = menu.SplitName;
            foreach (var d in menu.Days) Days.Add(new DayCardViewModel(d, menu.IsRestToday, this));
            foreach (var w in menu.Week) Week.Add(new WeekdayTileViewModel(w));
            ShowEmpty = false;
            HasSplit = true;
            SplitName = menu.SplitName;
            IsRestToday = menu.IsRestToday;
            TodayNote = menu.IsRestToday ? "today is a rest day"
                      : $"today is {menu.Days.FirstOrDefault(d => d.IsToday)?.Name ?? "a training day"}";
            NextName = menu.Days.FirstOrDefault(d => d.IsNext)?.Name;
            Subtitle = menu.IsRestToday ? $"{menu.SplitName} · today is a rest day" : menu.SplitName;
        }
        catch (Exception)
        {
            Error = "Could not load your workouts.";
        }
    }

    internal void Open(DayCardViewModel card) => _shell().ShowDay(card.Day, _splitName);

    [RelayCommand] private void ChooseSplit() => _shell().ShowSplits();

    /// <summary>The custom-exercises page belongs to a day (it adds to it): today's, else the first.</summary>
    [RelayCommand]
    private void CustomExercises()
    {
        if ((Days.FirstOrDefault(d => d.IsToday) ?? Days.FirstOrDefault()) is { } card)
            _shell().ShowCustomExercises(card.Day, _splitName);
    }
    [RelayCommand] private void Back() => _shell().ShowHome();
}