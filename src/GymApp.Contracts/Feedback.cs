using System.Text.RegularExpressions;

namespace GymApp.Contracts;

/// <summary>Sent as <c>problem</c> or <c>idea</c>.</summary>
public enum FeedbackKind { Problem, Idea }

/// <summary>A note from the app's Feedback popup. <see cref="Report"/> is the error report a problem can carry
/// (the app, the system and errors.log), already passed through <see cref="FeedbackRules.Scrub"/>.</summary>
public sealed record FeedbackRequest(FeedbackKind Kind, string Text, string? Report = null);

/// <summary>Limits and cleanup shared by the app (checks and scrubs before sending) and the server (checks and scrubs
/// again on arrival).</summary>
public static partial class FeedbackRules
{
    public const int MaxTextLength = 5_000;
    /// <summary>The app cuts its report to the newest 100,000 characters; this leaves room for the header.</summary>
    public const int MaxReportLength = 110_000;

    public const string EmptyMessage = "Write a few words first.";
    public const string TooLongMessage = "Keep it under 5,000 characters.";
    public const string ReportOnIdeaMessage = "Only a problem can carry an error report.";
    public const string ReportTooLongMessage = "The error report is too long.";

    /// <summary>The user's name in file paths of stack traces (/Users/name, /home/name, C:\Users\name) becomes
    /// "&lt;user&gt;": a report should say where the app broke, not whose computer it was.</summary>
    public static string Scrub(string text) => UserFolder().Replace(text, m => m.Groups["prefix"].Value + "<user>");

    // Unix folders end at a slash or whitespace; Windows ones may hold spaces ("Maria Lopez"), so they end at a backslash
    [GeneratedRegex(@"(?<prefix>/Users/|/home/)(?!<user>)[^/\s:]+|(?<prefix>[A-Za-z]:\\Users\\)(?!<user>)[^\\\r\n:]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex UserFolder();
}
