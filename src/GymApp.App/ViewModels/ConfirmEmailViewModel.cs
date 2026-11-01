using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Services;

namespace GymApp.App.ViewModels;

/// <summary>Enter the code from the email: right after signing up (Later moves on to the profile setup), or from the
/// reminder on Profile. Confirming is never required: an unconfirmed account works the same, and a reset code goes to its address too.</summary>
public partial class ConfirmEmailViewModel(IAccountService accounts, SessionService session, Func<MainViewModel> shell) : ViewModelBase
{
    public const string SentNotice = "We sent a new code.";

    public bool AfterSignUp { get; set; }
    public string Intro => $"We sent a {Contracts.EmailCodeRules.Length}-digit code to {session.CurrentUser?.Email}. If you can't see it, check your spam folder.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand), nameof(ResendCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty] public partial string Code { get; set; } = "";
    [ObservableProperty] public partial string? Error { get; set; }
    [ObservableProperty] public partial string? Notice { get; set; }

    private bool CanAct() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task ConfirmAsync()
    {
        if (await RunAsync(() => accounts.ConfirmEmailAsync(Code))) Leave();
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task ResendAsync()
    {
        if (await RunAsync(accounts.SendConfirmCodeAsync)) Notice = SentNotice;
    }

    [RelayCommand] private void Later() => Leave();

    private void Leave()
    {
        if (AfterSignUp) shell().ShowProfileSetup();
        else shell().GoBack();
    }

    private async Task<bool> RunAsync(Func<Task<AccountResult>> action)
    {
        Error = Notice = null;
        IsBusy = true;
        try
        {
            var r = await action();
            Error = r.Error;
            return r.Success;
        }
        catch (Exception)
        {
            Error = "Something went wrong. Try again.";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
