using System;
using CommunityToolkit.Mvvm.Input;
using GymApp.Desktop.Services;

namespace GymApp.Desktop.ViewModels;

public partial class HomeViewModel : ViewModelBase
{
    private readonly SessionService _session;
    private readonly Func<MainViewModel> _shell;

    public HomeViewModel(SessionService session, Func<MainViewModel> shell)
    {
        _session = session;
        _shell = shell;
    }

    public string Welcome => $"Welcome, {_session.CurrentUser?.Username}!";

    [RelayCommand]
    private void Logout() => _session.SignOut();

    [RelayCommand]
    private void ShowSplits() => _shell().ShowSplits();
}