using GymApp.App.ViewModels;
using GymApp.Contracts;
using GymApp.Data.Models;
using GymApp.Services;

namespace GymApp.Tests;

public class EmailScreensTests
{
    private sealed class FakeAccounts : IAccountService
    {
        public AccountResult Next = new(true);
        public List<string> Calls { get; } = new();

        private Task<AccountResult> Answer(string call) { Calls.Add(call); return Task.FromResult(Next); }

        public bool? EmailConfirmed => false;
        public Task<AccountResult> SendConfirmCodeAsync() => Answer("send");
        public Task<AccountResult> ConfirmEmailAsync(string code) => Answer($"confirm {code}");
        public Task<AccountResult> RequestPasswordResetAsync(string email) => Answer($"request {email}");
        public Task<AccountResult> ResetPasswordAsync(string email, string code, string newPassword) => Answer($"reset {email} {code} {newPassword}");
        public Task<AccountResult> RegisterAsync(string username, string email, string password) => throw new NotSupportedException();
        public Task<AccountResult> SignInAsync(string login, string password) => throw new NotSupportedException();
        public Task<AccountResult> MoveOnlineAsync(int localUserId, string password, string? email = null, string? username = null) => throw new NotSupportedException();
        public Task<bool> RestoreAsync() => throw new NotSupportedException();
        public Task LogOutAsync() => throw new NotSupportedException();
        public Task<AccountResult> ChangeEmailAsync(string newEmail, string currentPassword) => throw new NotSupportedException();
        public Task<AccountResult> ChangePasswordAsync(string currentPassword, string newPassword) => throw new NotSupportedException();
        public Task<AccountResult> DeleteAccountAsync(string password) => throw new NotSupportedException();
    }

    private readonly FakeAccounts _accounts = new();

    [Fact] public async Task Confirm_AWrongCode_StaysAndSaysSo()
    {
        var vm = new ConfirmEmailViewModel(_accounts, new SessionService(), () => null!) { Code = "123456" };
        _accounts.Next = AccountResult.Fail(EmailCodeRules.WrongCodeMessage);

        await vm.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal("confirm 123456", Assert.Single(_accounts.Calls));
        Assert.Equal(EmailCodeRules.WrongCodeMessage, vm.Error);
    }

    [Fact] public async Task TheFirstCode_GoesOutWhenAsked_AndAFailureShows()
    {
        var vm = new ConfirmEmailViewModel(_accounts, new SessionService(), () => null!);
        _accounts.Next = AccountResult.Fail(EmailCodeRules.NotSentMessage);

        await vm.SendFirstCodeAsync();

        Assert.Equal("send", Assert.Single(_accounts.Calls));
        Assert.Equal(EmailCodeRules.NotSentMessage, vm.Error);
    }

    [Fact] public async Task Resend_SaysItWentOut_OrWhyNot()
    {
        var vm = new ConfirmEmailViewModel(_accounts, new SessionService(), () => null!);

        await vm.ResendCommand.ExecuteAsync(null);
        Assert.Equal(ConfirmEmailViewModel.SentNotice, vm.Notice);

        _accounts.Next = AccountResult.Fail(EmailCodeRules.TooSoonMessage);
        await vm.ResendCommand.ExecuteAsync(null);
        Assert.Null(vm.Notice);
        Assert.Equal(EmailCodeRules.TooSoonMessage, vm.Error);
    }

    [Fact] public async Task Forgot_AsksForTheEmail_ThenTheCode()
    {
        var vm = new ForgotPasswordViewModel(_accounts, () => null!) { Email = "a@example.com" };
        Assert.True(vm.IsAskingEmail);

        await vm.SendCodeCommand.ExecuteAsync(null);

        Assert.True(vm.IsEnteringCode);
        Assert.Equal(ForgotPasswordViewModel.SentNotice, vm.Notice);
    }

    [Fact] public async Task Forgot_ARefusedEmail_StaysOnTheFirstStep()
    {
        var vm = new ForgotPasswordViewModel(_accounts, () => null!) { Email = "nope" };
        _accounts.Next = AccountResult.Fail("Enter a valid email address.", FormField.Email);

        await vm.SendCodeCommand.ExecuteAsync(null);

        Assert.True(vm.IsAskingEmail);
        Assert.Equal("Enter a valid email address.", vm.Error);
    }

    [Fact] public async Task Forgot_AWeakPassword_ShowsUnderTheField()
    {
        var vm = new ForgotPasswordViewModel(_accounts, () => null!) { Email = "a@example.com", Code = "123456", NewPassword = "x" };
        _accounts.Next = AccountResult.Fail("Too short.", FormField.NewPassword);

        await vm.ResetCommand.ExecuteAsync(null);

        Assert.Equal("Too short.", vm.NewPasswordError);
        Assert.Null(vm.Error);
    }

    [Fact] public async Task Forgot_UseAnotherEmail_GoesBackAndClearsTheCode()
    {
        var vm = new ForgotPasswordViewModel(_accounts, () => null!) { Email = "a@example.com", Code = "123456" };
        await vm.SendCodeCommand.ExecuteAsync(null);

        vm.UseAnotherEmailCommand.Execute(null);

        Assert.True(vm.IsAskingEmail);
        Assert.Equal("", vm.Code);
    }
}
