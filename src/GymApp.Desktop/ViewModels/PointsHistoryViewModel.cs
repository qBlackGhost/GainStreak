using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Core;
using GymApp.Services;

namespace GymApp.Desktop.ViewModels;

public record PointsRow(string DateText, string ReasonText, string AmountText);

/// <summary>The points ledger, newest first, with the total and level at the top.</summary>
public partial class PointsHistoryViewModel : ViewModelBase
{
    private readonly PointsService _points;
    private readonly SessionService _session;
    private readonly Func<MainViewModel> _shell;

    public PointsHistoryViewModel(PointsService points, SessionService session, Func<MainViewModel> shell)
    {
        _points = points;
        _session = session;
        _shell = shell;
        _ = LoadAsync();
    }

    public ObservableCollection<PointsRow> Rows { get; } = new();

    [ObservableProperty] public partial string TotalText { get; set; } = "";
    [ObservableProperty] public partial string LevelText { get; set; } = "";
    [ObservableProperty] public partial bool IsEmpty { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }

    private async Task LoadAsync()
    {
        if (_session.CurrentUser is not { } user) return;
        try
        {
            var o = await _points.GetAsync(user.Id);
            if (o is null) return;

            TotalText = $"{N(o.Total)} points";
            LevelText = $"Level {o.Level.Number} · {o.Level.Name}";
            Rows.Clear();
            foreach (var line in o.History)
                Rows.Add(new PointsRow(
                    line.Date.ToString("ddd d MMM yyyy", CultureInfo.InvariantCulture),
                    PointsCalculator.DisplayName(line.Reason),
                    $"+{N(line.Amount)}"));
            IsEmpty = Rows.Count == 0;
        }
        catch (Exception)
        {
            Error = "Could not load your points.";
        }
    }

    private static string N(int n) => n.ToString("N0", CultureInfo.InvariantCulture);

    [RelayCommand]
    private void Back() => _shell().ShowHome();
}
