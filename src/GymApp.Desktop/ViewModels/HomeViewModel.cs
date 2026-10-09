using CommunityToolkit.Mvvm.Input;
using GymApp.Desktop.Services;

namespace GymApp.Desktop.ViewModels;

public partial class HomeViewModel : ViewModelBase
{
    private readonly SessionService _session;

    public HomeViewModel(SessionService session) => _session = session;

    public string Welcome => $"Welcome, {_session.CurrentUser?.Username}!";

    [RelayCommand]
    private void Logout() => _session.SignOut();
}