using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Core;
using GymApp.Services;

namespace GymApp.App.ViewModels;

/// <summary>A colour to pick.</summary>
public partial class AvatarColorOption(string color) : ObservableObject
{
    public string Color { get; } = color;
    [ObservableProperty] public partial bool IsSelected { get; set; }
}

/// <summary>An icon to pick, shown in the chosen colour.</summary>
public partial class AvatarIconOption(string icon) : ObservableObject
{
    public string Icon { get; } = icon;
    [ObservableProperty] public partial string Preview { get; set; } = "";
    [ObservableProperty] public partial bool IsSelected { get; set; }
}

/// <summary>The popup that picks the avatar: a colour, and initials or an icon. The ring comes from the level, so it
/// only shows in the preview.</summary>
public partial class AvatarPickerViewModel : ViewModelBase
{
    private readonly ProfileService _profiles;
    private readonly SessionService _session;
    private readonly Func<MainViewModel> _shell;
    private Func<Task> _saved = () => Task.CompletedTask;
    private string _color = AvatarChoice.Colors[0];
    private string _icon = AvatarChoice.Icons[0];

    public IReadOnlyList<AvatarColorOption> Colors { get; } = AvatarChoice.Colors.Select(c => new AvatarColorOption(c)).ToList();
    public IReadOnlyList<AvatarIconOption> Icons { get; } = AvatarChoice.Icons.Select(i => new AvatarIconOption(i)).ToList();

    [ObservableProperty] public partial string? Preview { get; set; }
    [ObservableProperty] public partial string Initials { get; set; } = "";
    [ObservableProperty] public partial int Level { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }

    /// <summary>The Initials / Icon switch.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesIcon))]
    public partial int ModeIndex { get; set; }

    public bool UsesIcon => ModeIndex == 1;

    public AvatarPickerViewModel(ProfileService profiles, SessionService session, Func<MainViewModel> shell)
    {
        _profiles = profiles; _session = session; _shell = shell;
    }

    /// <summary>Call right after resolving.</summary>
    public void Load(string? avatar, string initials, int level, Func<Task> saved)
    {
        _saved = saved;
        Initials = initials;
        Level = level;
        var choice = AvatarChoice.Parse(avatar);
        _color = choice?.Color ?? _color;
        _icon = choice?.Icon ?? _icon;
        ModeIndex = choice?.Icon is null ? 0 : 1;
        Refresh();
    }

    partial void OnModeIndexChanged(int value) => Refresh();

    private void Refresh()
    {
        foreach (var c in Colors) c.IsSelected = c.Color == _color;
        foreach (var i in Icons)
        {
            i.IsSelected = i.Icon == _icon;
            i.Preview = new AvatarChoice(_color, i.Icon).ToString();
        }
        Preview = Choice.ToString();
    }

    private AvatarChoice Choice => new(_color, UsesIcon ? _icon : null);

    [RelayCommand] private void ShowInitials() => ModeIndex = 0;
    [RelayCommand] private void ShowIcon() => ModeIndex = 1;

    [RelayCommand]
    private void SelectColor(AvatarColorOption option)
    {
        _color = option.Color;
        Refresh();
    }

    [RelayCommand]
    private void SelectIcon(AvatarIconOption option)
    {
        _icon = option.Icon;
        Refresh();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        Error = null;
        var r = await _profiles.SetAvatarAsync(_session.CurrentUser!.Id, Choice);
        if (!r.Success) { Error = r.Error; return; }
        _shell().ClosePopup();
        await _saved();
    }

    [RelayCommand] private void Cancel() => _shell().ClosePopup();
}
