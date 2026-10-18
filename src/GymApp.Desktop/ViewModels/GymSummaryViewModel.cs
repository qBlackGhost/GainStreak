using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Core;
using GymApp.Services;

namespace GymApp.Desktop.ViewModels;

/// <summary>One gym in the month, with a bar relative to the most visited one.</summary>
public record GymVisitRow(string Name, string? Address, int Visits, double Share, bool IsActive, bool IsCustom)
{
    public string VisitsText => Visits == 1 ? "1 visit" : $"{Visits} visits";
    public bool HasAddress => !string.IsNullOrEmpty(Address);
    public bool IsInactive => !IsActive;
}

/// <summary>Gyms visited in a calendar month, recomputed from the logs each time. No future months.</summary>
public partial class GymSummaryViewModel : ViewModelBase
{
    private readonly GymSummaryService _summaries;
    private readonly SessionService _session;
    private readonly Func<MainViewModel> _shell;
    private bool _backToProfile;

    public GymSummaryViewModel(GymSummaryService summaries, SessionService session, Func<MainViewModel> shell)
    {
        _summaries = summaries; _session = session; _shell = shell;
        (Year, Month) = (Today.Year, Today.Month);
        _ = LoadAsync();
    }

    private static DateOnly Today => AppClock.Today;

    public void OpenedFromProfile() => _backToProfile = true;

    public int Year { get; private set; }
    public int Month { get; private set; }
    public ObservableCollection<GymVisitRow> Gyms { get; } = new();

    [ObservableProperty] public partial string MonthTitle { get; set; } = "";
    [ObservableProperty] public partial string TotalsText { get; set; } = "";
    [ObservableProperty] public partial string NoGymText { get; set; } = "";
    [ObservableProperty] public partial bool HasNoGym { get; set; }
    [ObservableProperty] public partial bool HasGyms { get; set; }
    [ObservableProperty] public partial string EmptyText { get; set; } = "";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(NextMonthCommand))] public partial bool CanGoNext { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }

    private async Task LoadAsync()
    {
        if (_session.CurrentUser is not { } user) return;
        MonthTitle = GymSummaries.MonthTitle(Year, Month);
        CanGoNext = !GymSummaries.IsFuture(GymSummaries.Shift(Year, Month, 1).Year, GymSummaries.Shift(Year, Month, 1).Month, Today);
        try
        {
            var s = await _summaries.GetMonthAsync(user.Id, Year, Month);
            Error = null;

            Gyms.Clear();
            var max = s.Gyms.Count > 0 ? s.Gyms[0].Visits : 1;
            foreach (var g in s.Gyms)
                Gyms.Add(new GymVisitRow(g.Name, g.Address, g.Visits, (double)g.Visits / max, g.IsActive, g.IsCustom));
            HasGyms = Gyms.Count > 0;

            TotalsText = s.IsEmpty ? ""
                : $"{Plural(s.TotalWorkouts, "workout")} · {Plural(s.DistinctGyms, "gym")}";
            HasNoGym = s.NoGymLogged > 0;
            NoGymText = $"No gym logged: {Plural(s.NoGymLogged, "workout")}";
            EmptyText = s.IsEmpty
                ? (Year, Month) == (Today.Year, Today.Month) ? "No workouts logged this month yet." : "No workouts logged in this month."
                : HasGyms ? "" : "No gyms logged for these workouts.";
        }
        catch (Exception)
        {
            Error = "Could not load the summary.";
        }
    }

    private static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";

    [RelayCommand]
    private Task PreviousMonthAsync()
    {
        (Year, Month) = GymSummaries.Shift(Year, Month, -1);
        return LoadAsync();
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private Task NextMonthAsync()
    {
        var (y, m) = GymSummaries.Shift(Year, Month, 1);
        if (GymSummaries.IsFuture(y, m, Today)) return Task.CompletedTask;
        (Year, Month) = (y, m);
        return LoadAsync();
    }

    [RelayCommand]
    private void Back()
    {
        if (_backToProfile) _shell().ShowProfile();
        else _shell().ShowHome();
    }
}
