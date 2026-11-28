using System;
using System.Globalization;
using System.Text;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using GymApp.Contracts;
using GymApp.Services;

namespace GymApp.App.ViewModels;

/// <summary>
/// The feedback popup, opened from the sidebar's Support section on any screen. "Report a problem" or "Suggest an
/// idea": the user writes a few words and Send passes them to <see cref="IFeedbackService"/> (the server, which
/// forwards them to the developer's Discord), a problem with the error report from <see cref="ErrorLog.BuildReport"/>
/// unless that's turned off. When sending fails the note stays, and "Copy instead" puts it on the clipboard to paste
/// into a message; the view does the clipboard, this builds the text (<see cref="Compose"/>).
/// </summary>
public partial class FeedbackViewModel(ErrorLog errorLog, IFeedbackService feedback, IPlatform platform) : ViewModelBase
{
    public const string EmptyError = FeedbackRules.EmptyMessage;
    public const string SentText = "Sent. Thanks! The developer reads every one.";
    public const string CopiedText = "Copied. Paste it into a message to the developer.";

    private Action _closed = () => { };

    /// <summary>Raised when the popup is shown, so the view can move focus into it (Escape then closes it).</summary>
    public event Action? Opened;

    /// <summary>"GainStreak 0.15.0 · read by the developer", under the title.</summary>
    public string SubtitleText { get; } = $"GainStreak {AppVersion.Current} · read by the developer";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KindIndex), nameof(IsProblem), nameof(IsIdea), nameof(Prompt), nameof(Placeholder))]
    public partial FeedbackKind Kind { get; set; }

    public int KindIndex => (int)Kind;
    public bool IsProblem => Kind == FeedbackKind.Problem;
    public bool IsIdea => Kind == FeedbackKind.Idea;

    public string Prompt => IsProblem ? "What happened?" : "What would make GainStreak better?";
    public string Placeholder => IsProblem
        ? "What you did, what you expected, and what happened instead"
        : "A feature, a change, something that bugs you";

    /// <summary>What the user wrote, kept per tab so switching doesn't lose it.</summary>
    [ObservableProperty] public partial string ProblemText { get; set; } = "";
    [ObservableProperty] public partial string IdeaText { get; set; } = "";

    /// <summary>A problem carries the error report unless this is turned off.</summary>
    [ObservableProperty] public partial bool AttachReport { get; set; } = true;

    /// <summary>"Copied…", or why not.</summary>
    [ObservableProperty] public partial string? Message { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial bool IsSending { get; set; }

    public const string FooterDefault = "Sent to the developer with your username.";

    /// <summary>"You can send again at 14:35." during the 10-minute wait after a message (0.28.1), when Send is greyed out.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyPropertyChangedFor(nameof(IsWaiting), nameof(FooterText))]
    public partial string? WaitNotice { get; private set; }

    public bool IsWaiting => WaitNotice is not null;

    /// <summary>The footer beside Send: where it goes, or the wait.</summary>
    public string FooterText => WaitNotice ?? FooterDefault;

    private CancellationTokenSource? _waitEnd;

    /// <summary>Reads the wait from <see cref="IFeedbackService.WaitUntil"/>, and lifts it on its own when it ends.</summary>
    private void UpdateWait()
    {
        _waitEnd?.Cancel();
        _waitEnd = null;
        if (feedback.WaitUntil is not { } until)
        {
            WaitNotice = null;
            return;
        }
        WaitNotice = SendAgainText(until);
        var cts = _waitEnd = new CancellationTokenSource();
        _ = LiftAsync(until - DateTime.UtcNow, cts.Token);

        async Task LiftAsync(TimeSpan delay, CancellationToken token)
        {
            try { await Task.Delay(delay < TimeSpan.Zero ? TimeSpan.Zero : delay, token); }
            catch (OperationCanceledException) { return; }
            UpdateWait();
        }
    }

    /// <summary>"You can send again at 14:35.", in the phone's own time and format.</summary>
    public static string SendAgainText(DateTime untilUtc, string what = "send again") =>
        $"You can {what} at {untilUtc.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)}.";

    /// <summary>"Copy instead" shows after a failed send, so the note can still reach the developer.</summary>
    [ObservableProperty] public partial bool CanCopyInstead { get; set; }

    /// <summary>"See what's sent": the error report as it would go, user names already taken out of file paths.</summary>
    [ObservableProperty] public partial bool IsReportPreviewOpen { get; set; }
    [ObservableProperty] public partial string ReportPreview { get; set; } = "";

    [RelayCommand]
    private void ToggleReportPreview()
    {
        if (!IsReportPreviewOpen) ReportPreview = FeedbackRules.Scrub(errorLog.BuildReport());
        IsReportPreviewOpen = !IsReportPreviewOpen;
    }

    partial void OnKindChanged(FeedbackKind value)
    {
        Message = Error = null;
        CanCopyInstead = false;
    }

    [RelayCommand] private void ShowProblem() => Kind = FeedbackKind.Problem;
    [RelayCommand] private void ShowIdea() => Kind = FeedbackKind.Idea;

    /// <summary>Shows the popup on <paramref name="kind"/>, keeping anything written before.</summary>
    public void Open(Action closed, FeedbackKind kind = FeedbackKind.Problem)
    {
        _closed = closed;
        Kind = kind;
        Message = Error = null;
        CanCopyInstead = IsReportPreviewOpen = false;
        UpdateWait();
        Opened?.Invoke();
    }

    [RelayCommand]
    private void Close()
    {
        _waitEnd?.Cancel();
        _closed();
    }

    private bool CanSend() => !IsSending && !IsWaiting;

    /// <summary>Sends the current tab's note. On success it's cleared; otherwise it stays, with the reason and "Copy instead".</summary>
    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        Message = null;
        var text = (IsProblem ? ProblemText : IdeaText).Trim();
        if (text.Length == 0)
        {
            Error = EmptyError;
            return;
        }
        Error = null;
        IsSending = true;
        try
        {
            var r = await feedback.SendAsync(Kind, text, IsProblem && AttachReport ? errorLog.BuildReport() : null);
            UpdateWait();
            if (!r.Success)
            {
                Error = IsWaiting ? null : r.Error;   // the wait says it already, under Send
                CanCopyInstead = true;
                return;
            }
            if (IsProblem) ProblemText = ""; else IdeaText = "";
            CanCopyInstead = IsReportPreviewOpen = false;
            Message = SentText;
        }
        finally
        {
            IsSending = false;
        }
    }

    /// <summary>The text to copy, or null (with <see cref="Error"/> set) when nothing was written.</summary>
    public string? Compose(DateTime? now = null)
    {
        Message = null;
        var text = (IsProblem ? ProblemText : IdeaText).Trim();
        if (text.Length == 0)
        {
            Error = EmptyError;
            return null;
        }
        Error = null;

        var b = new StringBuilder()
            .AppendLine(IsProblem ? "GainStreak feedback: a problem" : "GainStreak feedback: an idea")
            .AppendLine()
            .AppendLine(text);
        if (IsProblem && AttachReport)
            b.AppendLine().AppendLine("----").Append(errorLog.BuildReport(now));
        else
            b.AppendLine().Append("App: ").AppendLine(errorLog.AppInfo ?? $"GainStreak {AppVersion.Current}");
        return FeedbackRules.Scrub(b.ToString());
    }

    /// <summary>The view calls this with whether the clipboard took the text. Copied text is cleared from its tab.</summary>
    public void Copied(bool ok)
    {
        if (!ok)
        {
            Error = "Couldn't copy it. Try again.";
            return;
        }
        if (IsProblem) ProblemText = ""; else IdeaText = "";
        CanCopyInstead = false;
        Message = CopiedText;
    }

    /// <summary>"Show log file": only where the system can open a folder (not on phones).</summary>
    public bool CanShowLogFolder => platform.CanShowLogFolder;

    /// <summary>Opens the data folder (where errors.log is) in Finder or Explorer, to attach the file itself.</summary>
    [RelayCommand]
    private void ShowLogFile()
    {
        try
        {
            platform.ShowLogFolder();
        }
        catch (Exception)
        {
            Error = $"Couldn't open the folder. The log is {ErrorLog.FileName} in {platform.DataDirectory}.";
        }
    }
}
