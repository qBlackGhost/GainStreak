using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Services;

namespace GymApp.App.ViewModels;

/// <summary>Forgot password, from Log in: the email first, then the code and a new password. It says the same whether or
/// not an account has that email, so nobody can use it to find out.</summary>
public partial class ForgotPasswordViewModel(IAccountService accounts, Func<MainViewModel> shell) : ViewModelBase
{
    public const string SentNotice = "If an account uses that email, a code is on its way. Check your spam folder too.";
    public const string DoneNotice = "Your password is changed. Sign in with the new one.";

    public PasswordVisibility PasswordView { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAskingEmail))]
    public partial bool IsEnteringCode { get; set; }

    public bool IsAskingEmail => !IsEnteringCode;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCodeCommand), nameof(ResetCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty] public partial string Email { get; set; } = "";
    [ObservableProperty] public partial string Code { get; set; } = "";
    [ObservableProperty] public partial string NewPassword { get; set; } = "";
    [ObservableProperty] public partial string? Error { get; set; }
    [ObservableProperty] public partial string? NewPasswordError { get; set; }
    [ObservableProperty] public partial string? Notice { get; set; }

    private bool CanAct() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task SendCodeAsync()
    {
        if (await RunAsync(() => accounts.RequestPasswordResetAsync(Email)) is not { Success: true }) return;
        IsEnteringCode = true;
        Notice = SentNotice;
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task ResetAsync()
    {
        if (await RunAsync(() => accounts.ResetPasswordAsync(Email, Code, NewPassword)) is { Success: true })
            shell().ShowLogin(DoneNotice);
    }

    [RelayCommand] private void UseAnotherEmail()
    {
        Error = NewPasswordError = Notice = null;
        Code = NewPassword = "";
        IsEnteringCode = false;
    }

    [RelayCommand] private void Back() => shell().ShowLogin();

    private async Task<AccountResult?> RunAsync(Func<Task<AccountResult>> action)
    {
        Error = NewPasswordError = Notice = null;
        IsBusy = true;
        try
        {
            var r = await action();
            if (r.Field == FormField.NewPassword) NewPasswordError = r.Error;
            else Error = r.Error;
            return r;
        }
        catch (Exception)
        {
            Error = "Something went wrong. Try again.";
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
