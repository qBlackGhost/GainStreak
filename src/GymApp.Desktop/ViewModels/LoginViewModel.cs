using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Desktop.Services;

namespace GymApp.Desktop.ViewModels;

public partial class LoginViewModel : ViewModelBase
{
    private readonly AuthService _auth;
    private readonly SessionService _session;
    private readonly Func<MainViewModel> _shell;

    public LoginViewModel(AuthService auth, SessionService session, Func<MainViewModel> shell)
    {
        _auth = auth; _session = session; _shell = shell;
    }

    [ObservableProperty] public partial string Username { get; set; } = "";
    [ObservableProperty] public partial string Password { get; set; } = "";
    [ObservableProperty] public partial string? Error { get; set; }

    [RelayCommand]
    private async Task LoginAsync()
    {
        var result = await _auth.LoginAsync(Username, Password);
        if (result.Success) _session.SignIn(result.User!);
        else Error = result.Error;
    }

    [RelayCommand]
    private void GoToRegister() => _shell().ShowRegister();
}