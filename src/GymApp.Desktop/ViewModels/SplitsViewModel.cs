using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Desktop.Services;

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
        var user = _session.CurrentUser!;
        var presets = await _splits.GetPresetsAsync();
        var custom = await _splits.GetCustomAsync(user.Id);

        Presets.Clear(); Custom.Clear();
        foreach (var s in presets) Presets.Add(new(s, s.Id == user.SelectedSplitId, this));
        foreach (var s in custom) Custom.Add(new(s, s.Id == user.SelectedSplitId, this));

        CanAddCustom = custom.Count < SplitService.MaxCustomSplits;
        CustomCounter = $"{custom.Count} / {SplitService.MaxCustomSplits} custom splits";
    }

    internal async Task SelectAsync(SplitItemViewModel item)
    {
        var user = _session.CurrentUser!;
        if (!await _splits.SelectAsync(user.Id, item.Split.Id)) { Error = "Could not select that split."; return; }

        user.SelectedSplitId = item.Split.Id;   // keep the in-memory session user in sync
        foreach (var i in Presets.Concat(Custom)) i.IsSelected = i == item;
    }

    internal void Edit(SplitItemViewModel item) => _shell().ShowSplitEditor(item.Split);

    internal async Task DeleteAsync(SplitItemViewModel item)
    {
        var user = _session.CurrentUser!;
        await _splits.DeleteAsync(user.Id, item.Split.Id);
        if (user.SelectedSplitId == item.Split.Id) user.SelectedSplitId = null;
        await LoadAsync();
    }

    [RelayCommand] private void New() => _shell().ShowSplitEditor(null);
    [RelayCommand] private void Back() => _shell().ShowHome();
}