namespace GymApp.Core;

/// <summary>Rules for new accounts. Existing usernames that don't follow them keep working; only registration checks.</summary>
public static class Credentials
{
    public const int MinUsernameLength = 3;
    public const int MaxUsernameLength = 32;
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 128;

    /// <summary>Letters, digits, '_', '.' and '-' only (no spaces, emoji or control characters), 3 to 32 of them.
    /// Expects the trimmed username.</summary>
    public static string? CheckUsername(string username)
    {
        if (username.Length < MinUsernameLength) return $"Username must be at least {MinUsernameLength} characters.";
        if (username.Length > MaxUsernameLength) return $"Username must be at most {MaxUsernameLength} characters.";
        if (!username.All(c => char.IsLetterOrDigit(c) || c is '_' or '.' or '-'))
            return "Use only letters, digits, '_', '.' and '-' in your username.";
        return null;
    }

    public static string? CheckPassword(string password)
    {
        if (password.Length < MinPasswordLength) return $"Password must be at least {MinPasswordLength} characters.";
        if (password.Length > MaxPasswordLength) return $"Password must be at most {MaxPasswordLength} characters.";
        if (string.IsNullOrWhiteSpace(password)) return "Password can't be only spaces.";
        return null;
    }
}
