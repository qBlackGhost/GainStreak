using GymApp.Contracts;
using GymApp.App.ViewModels;
using GymApp.Services;

namespace GymApp.Tests;

/// <summary>Records what was sent and answers with <see cref="Result"/>.</summary>
public sealed class FakeFeedbackService : IFeedbackService
{
    public List<(FeedbackKind Kind, string Text, string? Report)> Sent { get; } = new();
    public FeedbackSendResult Result { get; set; } = FeedbackSendResult.Ok;

    public Task<FeedbackSendResult> SendAsync(FeedbackKind kind, string text, string? report)
    {
        Sent.Add((kind, text, report));
        return Task.FromResult(Result);
    }
}

public class FeedbackViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gymapp-feedback-" + Guid.NewGuid().ToString("N"));
    private readonly ErrorLog _log;
    private readonly FeedbackViewModel _vm;
    private readonly FakeFeedbackService _service = new();
    private readonly FakePlatform _platform = new();
    private int _closed;

    public FeedbackViewModelTests()
    {
        _log = new ErrorLog(_dir, appInfo: "GainStreak 0.15.0 on Darwin (Arm64)");
        _vm = new FeedbackViewModel(_log, _service, _platform);
        _vm.Open(() => _closed++);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    // --- Show log file (through the head) ---

    [Fact] public void ShowLogFile_AsksThePlatform()
    {
        Assert.True(_vm.CanShowLogFolder);
        _vm.ShowLogFileCommand.Execute(null);
        Assert.Equal(1, _platform.LogFolderShown);
        Assert.Null(_vm.Error);
    }

    [Fact] public void ShowLogFile_WhenTheFolderCantOpen_SaysWhereTheLogIs()
    {
        _platform.FailToShowLogFolder = true;
        _vm.ShowLogFileCommand.Execute(null);
        Assert.Contains(ErrorLog.FileName, _vm.Error);
        Assert.Contains(_platform.DataDirectory, _vm.Error);
    }

    [Fact] public void ShowLogFile_IsHidden_WhereThePlatformCantOpenFolders()
    {
        _platform.CanShowLogFolder = false;   // the phones
        Assert.False(_vm.CanShowLogFolder);
    }

    // --- Send ---

    [Fact] public async Task Send_Problem_WithTheReport_ClearsTheNote_AndSaysSent()
    {
        _log.Write(new InvalidOperationException("boom"), "UI thread");
        _vm.ProblemText = "  The map froze.  ";

        await _vm.SendCommand.ExecuteAsync(null);

        var (kind, text, report) = Assert.Single(_service.Sent);
        Assert.Equal((FeedbackKind.Problem, "The map froze."), (kind, text));
        Assert.Contains("boom", report);
        Assert.Equal("", _vm.ProblemText);
        Assert.Equal(FeedbackViewModel.SentText, _vm.Message);
    }

    [Fact] public async Task Send_Idea_OrReportTurnedOff_SendsNoReport()
    {
        _vm.ProblemText = "a problem";
        _vm.AttachReport = false;
        await _vm.SendCommand.ExecuteAsync(null);
        _vm.ShowIdeaCommand.Execute(null);
        _vm.IdeaText = "an idea";
        await _vm.SendCommand.ExecuteAsync(null);

        Assert.All(_service.Sent, s => Assert.Null(s.Report));
        Assert.Equal(FeedbackKind.Idea, _service.Sent[1].Kind);
    }

    [Fact] public async Task Send_Failing_KeepsTheNote_AndOffersCopyInstead()
    {
        _service.Result = new FeedbackSendResult(false, "You're offline. Try again when you're connected.");
        _vm.ProblemText = "a problem";

        await _vm.SendCommand.ExecuteAsync(null);

        Assert.Equal("a problem", _vm.ProblemText);
        Assert.Equal("You're offline. Try again when you're connected.", _vm.Error);
        Assert.True(_vm.CanCopyInstead);

        _vm.Copied(true);
        Assert.False(_vm.CanCopyInstead);
        Assert.Equal(FeedbackViewModel.CopiedText, _vm.Message);
    }

    [Fact] public async Task Send_Nothing_Written_SendsNothing()
    {
        _vm.ProblemText = "  ";
        await _vm.SendCommand.ExecuteAsync(null);
        Assert.Empty(_service.Sent);
        Assert.Equal(FeedbackViewModel.EmptyError, _vm.Error);
    }

    [Fact] public void ReportPreview_IsScrubbed()
    {
        _log.Write(new InvalidOperationException("at /Users/cristi/GymApp/Foo.cs:line 3"), "UI thread");
        _vm.ToggleReportPreviewCommand.Execute(null);

        Assert.True(_vm.IsReportPreviewOpen);
        Assert.Contains("/Users/<user>/GymApp/Foo.cs", _vm.ReportPreview);
        Assert.DoesNotContain("cristi", _vm.ReportPreview);
    }

    // --- Copy instead ---

    [Fact] public void Problem_AttachesTheErrorReport()
    {
        _log.Write(new InvalidOperationException("boom"), "UI thread");
        _vm.ProblemText = "  The map froze after picking a gym.  ";

        var text = _vm.Compose()!;

        Assert.StartsWith("GainStreak feedback: a problem", text);
        Assert.Contains("The map froze after picking a gym.", text);
        Assert.Contains("GainStreak error report", text);
        Assert.Contains("boom", text);
    }

    [Fact] public void Problem_WithoutTheReport_HasOnlyTheAppLine()
    {
        _log.Write(new InvalidOperationException("boom"), "UI thread");
        _vm.ProblemText = "Something odd";
        _vm.AttachReport = false;

        var text = _vm.Compose()!;

        Assert.DoesNotContain("boom", text);
        Assert.Contains("App: GainStreak 0.15.0 on Darwin (Arm64)", text);
    }

    [Fact] public void Idea_NeverAttachesTheReport()
    {
        _log.Write(new InvalidOperationException("boom"), "UI thread");
        _vm.ShowIdeaCommand.Execute(null);
        _vm.IdeaText = "Dark mode for the map";

        var text = _vm.Compose()!;

        Assert.StartsWith("GainStreak feedback: an idea", text);
        Assert.DoesNotContain("boom", text);
    }

    [Fact] public void Nothing_Written_IsAnError_AndCopiesNothing()
    {
        _vm.ProblemText = "   ";
        Assert.Null(_vm.Compose());
        Assert.Equal(FeedbackViewModel.EmptyError, _vm.Error);
    }

    [Fact] public void EachTab_KeepsItsOwnNote_AndACopyClearsOnlyThatOne()
    {
        _vm.ProblemText = "a problem";
        _vm.ShowIdeaCommand.Execute(null);
        _vm.IdeaText = "an idea";

        _vm.Copied(true);

        Assert.Equal(("a problem", ""), (_vm.ProblemText, _vm.IdeaText));
        Assert.Equal(FeedbackViewModel.CopiedText, _vm.Message);
        _vm.ShowProblemCommand.Execute(null);
        Assert.Null(_vm.Message);   // switching tabs clears the status
    }

    [Fact] public void FailedCopy_KeepsTheNote()
    {
        _vm.ProblemText = "a problem";
        _vm.Copied(false);
        Assert.Equal("a problem", _vm.ProblemText);
        Assert.NotNull(_vm.Error);
    }

    [Fact] public void Close_RunsTheCallback_AndOpenPicksTheTab()
    {
        _vm.CloseCommand.Execute(null);
        Assert.Equal(1, _closed);

        _vm.Open(() => { }, FeedbackKind.Idea);
        Assert.True(_vm.IsIdea);
        Assert.Equal(1, _vm.KindIndex);
    }
}
