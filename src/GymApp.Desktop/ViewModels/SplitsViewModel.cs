using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Services;

namespace GymApp.Desktop.ViewModels;

public partial class SplitsViewModel : ViewModelBase
{
    private readonly SplitService _splits;
    private readonly SessionService _session;
    private readonly ISplitUsageService _usage;
    private readonly Func<MainViewModel> _shell;

    public ObservableCollection<SplitItemViewModel> Presets { get; } = new();
    public ObservableCollection<SplitItemViewModel> Custom { get; } = new();

    [ObservableProperty] public partial string CustomCounter { get; set; } = "";
    [ObservableProperty] public partial string CustomCount { get; set; } = "";
    [ObservableProperty] public partial string SlotsLeftText { get; set; } = "";
    [ObservableProperty] public partial string ActiveName { get; set; } = "None";
    [ObservableProperty] public partial bool CanAddCustom { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }

    public SplitsViewModel(SplitService splits, SessionService session, ISplitUsageService usage, Func<MainViewModel> shell)
    {
        _splits = splits; _session = session; _usage = usage; _shell = shell;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var user = _session.CurrentUser!;
            var presets = await _splits.GetPresetsAsync();
            var custom = await _splits.GetCustomAsync(user.Id);

            Presets.Clear(); Custom.Clear();
            foreach (var s in presets) Presets.Add(new(s, s.Id == user.SelectedSplitId, this));
            foreach (var s in custom) Custom.Add(new(s, s.Id == user.SelectedSplitId, this));

            CanAddCustom = custom.Count < SplitService.MaxCustomSplits;
            CustomCounter = $"{custom.Count} / {SplitService.MaxCustomSplits} custom splits";
            CustomCount = $"{custom.Count} / {SplitService.MaxCustomSplits}";
            var left = SplitService.MaxCustomSplits - custom.Count;
            SlotsLeftText = $"Create a split · {left} slot{(left == 1 ? "" : "s")} left";
            ActiveName = Presets.Concat(Custom).FirstOrDefault(i => i.IsSelected)?.Name ?? "None";

        }
        catch (Exception)
        {
            Error = "Could not load your splits.";
            return;
        }
        await LoadUsageAsync();
    }

    /// <summary>Each preset's share of everyone with a split. Without the server's counts the cards just leave it out.</summary>
    private async Task LoadUsageAsync()
    {
        try
        {
            var usage = await _usage.GetAsync(_session.CurrentUser!.Id);
            if (usage is null || usage.Users == 0) return;
            foreach (var item in Presets)
            {
                var users = usage.Presets.FirstOrDefault(r => r.PresetId == item.Split.Id)?.Users ?? 0;
                item.UsageText = $"{SplitTags.Percent(100.0 * users / usage.Users)} of lifters";
            }
        }
        catch (Exception)
        {
            // optional, like the chart on the detail page
        }
    }

    // Selecting a split now happens on its detail page (variant + start day live there)
    internal void Open(SplitItemViewModel item) => _shell().ShowSplitDetail(item.Split);

    internal void Edit(SplitItemViewModel item) => _shell().ShowSplitEditor(item.Split);

    internal async Task DeleteAsync(SplitItemViewModel item)
    {
        try
        {
            var user = _session.CurrentUser!;
            await _splits.DeleteAsync(user.Id, item.Split.Id);
            if (user.SelectedSplitId == item.Split.Id)
            {
                user.SelectedSplitId = null;
                user.SelectedVariantId = null;
                user.SplitStartDay = null;
            }
            await LoadAsync();

        }
        catch (Exception)
        {
            Error = "Could not delete that split.";
        }
    }

    [RelayCommand] private void New() => _shell().ShowSplitEditor(null);
    [RelayCommand] private void Back() => _shell().ShowHome();
}
