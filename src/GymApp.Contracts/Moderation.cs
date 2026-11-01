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

    public static string Label(UserReportReason reason) => reason switch
    {
        UserReportReason.Username => "Offensive username",
        UserReportReason.Harassment => "Harassment",
        UserReportReason.Spam => "Spam or fake account",
        UserReportReason.SharedContent => "Something they shared",
        _ => "Something else"
    };
}
