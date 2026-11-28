using System;

namespace GymApp.Contracts;

/// <summary>Why a user is reported. Sent as a camelCase string.</summary>
public enum UserReportReason { Username, Harassment, Spam, SharedContent, Other }

public sealed record UserReportRequest(UserReportReason Reason, string? Note = null);

public sealed record BlockedUserResponse(int UserId, string Username, DateTime BlockedAt);

/// <summary>Limits and wording shared by the app and the server.</summary>
public static class UserReportRules
{
    public const int MaxNoteLength = 1_000;

    public const string TooLongMessage = "Keep it under 1,000 characters.";
    public const string SelfMessage = "You can't report yourself.";

    /// <summary>The wait between reports (0.28.1, anti-spam), counted apart from feedback. Like
    /// <see cref="FeedbackRules.Cooldown"/>, a 429 with <see cref="ProblemExtensions.RetryAt"/>.</summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(10);

    public static string TooSoonMessage(TimeSpan left) => $"You can send another report in {Cooldowns.Minutes(left)}.";

    public static string Label(UserReportReason reason) => reason switch
    {
        UserReportReason.Username => "Offensive username",
        UserReportReason.Harassment => "Harassment",
        UserReportReason.Spam => "Spam or fake account",
        UserReportReason.SharedContent => "Something they shared",
        _ => "Something else"
    };
}
