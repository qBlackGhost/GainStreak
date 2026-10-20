using System.Threading.Tasks;
using GymApp.Client;
using GymApp.Contracts;

namespace GymApp.Services;

/// <summary>The outcome of sending feedback: ok, or the message to show (the note is kept for another try).</summary>
public sealed record FeedbackSendResult(bool Success, string? Error = null)
{
    public static readonly FeedbackSendResult Ok = new(true);
}

/// <summary>Sends the Feedback popup's notes to the developer.</summary>
public interface IFeedbackService
{
    /// <param name="report">The error report for a problem (<see cref="ErrorLog.BuildReport"/>), or null.</param>
    Task<FeedbackSendResult> SendAsync(FeedbackKind kind, string text, string? report);
}

/// <summary>Through the social server (<c>POST /v1/feedback</c>), which stores it and forwards it to the developer's
/// Discord. Not queued: offline it says so and the note stays in the popup. The user's name is scrubbed from file paths
/// here, so it never leaves the device; the server scrubs again.</summary>
public class RemoteFeedbackService(SocialApiClient api) : IFeedbackService
{
    public async Task<FeedbackSendResult> SendAsync(FeedbackKind kind, string text, string? report)
    {
        text = FeedbackRules.Scrub(text.Trim());
        if (text.Length == 0) return new(false, FeedbackRules.EmptyMessage);
        if (text.Length > FeedbackRules.MaxTextLength) return new(false, FeedbackRules.TooLongMessage);

        var r = await api.SendFeedbackAsync(new FeedbackRequest(kind, text, report is null ? null : FeedbackRules.Scrub(report)));
        return r.IsOk ? FeedbackSendResult.Ok : new(false, r.Error);
    }
}

/// <summary>The TestBench's local mode has no server to send to.</summary>
public class LocalFeedbackService : IFeedbackService
{
    public const string NoServerMessage = "Sending needs the social server. Use \"Copy instead\".";

    public Task<FeedbackSendResult> SendAsync(FeedbackKind kind, string text, string? report) =>
        Task.FromResult(new FeedbackSendResult(false, NoServerMessage));
}
