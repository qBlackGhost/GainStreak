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
    [ObservableProperty] public partial string Email { get; set; } = "";
    [ObservableProperty] public partial string Password { get; set; } = "";
    [ObservableProperty] public partial string ConfirmPassword { get; set; } = "";
    [ObservableProperty] public partial string? Error { get; set; }
    [ObservableProperty] public partial string? UsernameError { get; set; }
    [ObservableProperty] public partial string? EmailError { get; set; }
    [ObservableProperty] public partial string? PasswordError { get; set; }
    [ObservableProperty] public partial string? ConfirmPasswordError { get; set; }

    [RelayCommand]
    private async Task RegisterAsync()
    {
        Error = UsernameError = EmailError = PasswordError = ConfirmPasswordError = null;
        if (Password != ConfirmPassword) { ConfirmPasswordError = "Passwords do not match."; return; }

        try
        {
            var result = await _auth.RegisterAsync(Username, Email, Password);
            if (result.Success) { _session.SignIn(result.User!); return; }   // log in straight after registering
            switch (result.Field)
            {
                case FormField.Username: UsernameError = result.Error; break;
                case FormField.Email: EmailError = result.Error; break;
                case FormField.Password: PasswordError = result.Error; break;
                default: Error = result.Error; break;
            }
        }
        catch (Exception)
        {
            Error = "Could not create your account. Try again.";
        }
    }

    [RelayCommand]
    private void GoToLogin() => _shell().ShowLogin();
}