using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Services;

namespace GymApp.Desktop.ViewModels;

public partial class SplitsViewModel : ViewModelBase
{
    private readonly SplitService _splits;
    private readonly SessionService _session;
    private readonly Func<MainViewModel> _shell;

    public ObservableCollection<SplitItemViewModel> Presets { get; } = new();
    public ObservableCollection<SplitItemViewModel> Custom { get; } = new();

    [ObservableProperty] public partial string CustomCounter { get; set; } = "";
    [ObservableProperty] public partial bool CanAddCustom { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }

    public SplitsViewModel(SplitService splits, SessionService session, Func<MainViewModel> shell)
    {
        _splits = splits; _session = session; _shell = shell;
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

        }
        catch (Exception)
        {
            Error = "Could not load your splits.";
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
