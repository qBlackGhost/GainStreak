using CommunityToolkit.Mvvm.Input;
using GymApp.Desktop.Services;

namespace GymApp.Desktop.ViewModels;

public partial class HomeViewModel : ViewModelBase
{
    private readonly SessionService _session;
    private readonly RememberMeService _remember;

    public HomeViewModel(SessionService session, RememberMeService remember)
    {
        _session = session;
        _remember = remember;
    }

    public string Welcome => $"Welcome, {_session.CurrentUser?.Username}!";

    [RelayCommand]
    private void Logout()
    {
        _remember.Forget();   // must run before SignOut, while CurrentUser is still set
        _session.SignOut();
    }
}