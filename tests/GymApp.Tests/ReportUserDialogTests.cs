using GymApp.App.ViewModels;
using GymApp.Contracts;
using GymApp.Services;
using GymApp.Social;

namespace GymApp.Tests;

public class ReportUserDialogTests : IDisposable
{
    private sealed class FakeReports : IUserReportService
    {
        public string? Refusal;
        public DateTime? WaitUntil { get; set; }
        public List<(int OtherId, UserReportReason Reason, string? Note)> Sent { get; } = new();

        public Task<string?> ReportAsync(int otherId, UserReportReason reason, string? note)
        {
            if (Refusal is null) Sent.Add((otherId, reason, note));
            return Task.FromResult(Refusal);
        }
    }

    private readonly TestDb _db = new();
    private readonly SessionService _session = new();
    private readonly FakeReports _reports = new();
    private readonly FriendService _friends;
    private readonly ReportUserDialogViewModel _vm;
    private readonly int _alice, _bob;
    private string? _notice;

    public ReportUserDialogTests()
    {
        _alice = _db.AddUser("alice");
        _bob = _db.AddUser("bob");
        using (var db = _db.CreateDbContext()) _session.SignIn(db.Users.Single(u => u.Id == _alice));
        _friends = new FriendService(new LocalSocialStore(_db));
        _vm = new ReportUserDialogViewModel(_friends, _reports, _session);
        _vm.Open(_bob, "bob", n => { _notice = n; return Task.CompletedTask; });
    }

    public void Dispose() => _db.Dispose();

    private void Pick(UserReportReason reason) => _vm.Reasons.Single(r => r.Reason == reason).ChooseCommand.Execute(null);

    [Fact] public async Task Send_NeedsAReason()
    {
        await _vm.SendCommand.ExecuteAsync(null);

        Assert.True(_vm.IsOpen);
        Assert.Equal("Pick a reason first.", _vm.Error);
        Assert.Empty(_reports.Sent);
    }

    [Fact] public void OnlyOneReasonIsPicked()
    {
        Pick(UserReportReason.Spam);
        Pick(UserReportReason.Harassment);

        Assert.Equal(UserReportReason.Harassment, _vm.Reasons.Single(r => r.IsSelected).Reason);
    }

    [Fact] public async Task Send_ReportsAndBlocks_ByDefault()
    {
        Pick(UserReportReason.Username);
        _vm.Note = "That name";

        await _vm.SendCommand.ExecuteAsync(null);

        Assert.Equal((_bob, UserReportReason.Username, "That name"), Assert.Single(_reports.Sent));
        Assert.Single(await _friends.GetBlockedAsync(_alice));
        Assert.False(_vm.IsOpen);
        Assert.Equal("Report sent, and bob is blocked.", _notice);
    }

    [Fact] public async Task Send_WithoutBlocking_OnlyReports()
    {
        Pick(UserReportReason.Spam);
        _vm.AlsoBlock = false;

        await _vm.SendCommand.ExecuteAsync(null);

        Assert.Single(_reports.Sent);
        Assert.Empty(await _friends.GetBlockedAsync(_alice));
    }

    [Fact] public async Task RefusedReport_StaysOpen_AndDoesntBlock()
    {
        _reports.Refusal = "You're offline.";
        Pick(UserReportReason.Spam);

        await _vm.SendCommand.ExecuteAsync(null);

        Assert.True(_vm.IsOpen);
        Assert.Equal("You're offline.", _vm.Error);
        Assert.Empty(await _friends.GetBlockedAsync(_alice));
    }

    [Fact] public void Open_StartsClean()
    {
        Pick(UserReportReason.Spam);
        _vm.Note = "old";
        _vm.AlsoBlock = false;

        _vm.Open(_bob, "bob", _ => Task.CompletedTask);

        Assert.DoesNotContain(_vm.Reasons, r => r.IsSelected);
        Assert.Equal("", _vm.Note);
        Assert.True(_vm.AlsoBlock);
    }

    // --- The 10-minute wait (0.28.1) ---

    [Fact] public async Task DuringTheWait_SendIsGreyedOut_ButBlockStillWorks()
    {
        _reports.WaitUntil = DateTime.UtcNow.AddMinutes(6);
        _vm.Open(_bob, "bob", n => { _notice = n; return Task.CompletedTask; });

        Assert.True(_vm.IsWaiting);
        Assert.StartsWith("You can send another report at ", _vm.WaitNotice);
        Assert.False(_vm.SendCommand.CanExecute(null));

        await _vm.BlockOnlyCommand.ExecuteAsync(null);
        await _vm.BlockOnlyCommand.ExecuteAsync(null);
        Assert.Equal("bob is blocked.", _notice);
    }

    [Fact] public async Task RefusedForTheWait_ShowsTheWait_InsteadOfTheMessage()
    {
        _vm.Reasons[0].ChooseCommand.Execute(null);
        _reports.Refusal = UserReportRules.TooSoonMessage(TimeSpan.FromMinutes(3));
        _reports.WaitUntil = DateTime.UtcNow.AddMinutes(3);

        await _vm.SendCommand.ExecuteAsync(null);

        Assert.True(_vm.IsOpen);
        Assert.Null(_vm.Error);
        Assert.True(_vm.IsWaiting);
    }
}
