using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Services;

namespace GymApp.Desktop.ViewModels;

public partial class RegisterViewModel : ViewModelBase
{
    private readonly AuthService _auth;
    private readonly SessionService _session;
    private readonly Func<MainViewModel> _shell;

    public RegisterViewModel(AuthService auth, SessionService session, Func<MainViewModel> shell)
    {
        _auth = auth; _session = session; _shell = shell;
    }

    [ObservableProperty] public partial string Username { get; set; } = "";
    [ObservableProperty] public partial string Password { get; set; } = "";
    [ObservableProperty] public partial string ConfirmPassword { get; set; } = "";
    [ObservableProperty] public partial string? Error { get; set; }

    [RelayCommand]
    private async Task RegisterAsync()
    {
        if (Password != ConfirmPassword) { Error = "Passwords do not match."; return; }

        var result = await _auth.RegisterAsync(Username, Password);
        if (result.Success) _session.SignIn(result.User!);   // log in straight after registering
        else Error = result.Error;
    }

    [RelayCommand]
    private void GoToLogin() => _shell().ShowLogin();
}