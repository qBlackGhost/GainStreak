namespace GymApp.Core;

/// <summary>Rules for new accounts. Existing usernames that don't follow them keep working; only registration checks.</summary>
public static class Credentials
{
    /// <summary>The terms new accounts agree to, from <c>site/terms.html</c>.</summary>
    public const string TermsUrl = "https://qblackghost.github.io/GainStreak-releases/terms.html";

    public const int MinUsernameLength = 3;
    public const int MaxUsernameLength = 32;
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 128;
    public const int MaxEmailLength = 254;

    public const string NeedsALetterMessage = "Your username needs at least one letter.";

    /// <summary>Letters, digits, '_', '.' and '-' only (no spaces, emoji or control characters), 3 to 32 of them, at
    /// least one letter (0.23.1: the avatar shows a letter, see <see cref="UserInitials"/>), and nothing
    /// <see cref="UsernameFilter"/> refuses. Expects the trimmed username.</summary>
    public static string? CheckUsername(string username)
    {
        if (username.Length < MinUsernameLength) return $"Username must be at least {MinUsernameLength} characters.";
        if (username.Length > MaxUsernameLength) return $"Username must be at most {MaxUsernameLength} characters.";
        if (!username.All(c => char.IsLetterOrDigit(c) || c is '_' or '.' or '-'))
            return "Use only letters, digits, '_', '.' and '-' in your username.";
        if (!username.Any(char.IsLetter)) return NeedsALetterMessage;
        if (!UsernameFilter.IsAllowed(username)) return "That username isn't allowed. Pick another one.";
        return null;
    }

    public static string? CheckPassword(string password)
    {
        if (password.Length < MinPasswordLength) return $"Password must be at least {MinPasswordLength} characters.";
        if (password.Length > MaxPasswordLength) return $"Password must be at most {MaxPasswordLength} characters.";
        if (string.IsNullOrWhiteSpace(password)) return "Password can't be only spaces.";
        return null;
    }

    /// <summary>The trimmed, lowercased form emails are stored and looked up in.</summary>
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    /// <summary>A plain shape check (name@domain.tld, no spaces). Nothing is sent, so the address isn't verified.
    /// Expects the normalized email.</summary>
    public static string? CheckEmail(string email)
    {
        if (email.Length == 0) return "Enter your email address.";
        if (email.Length > MaxEmailLength) return $"Email must be at most {MaxEmailLength} characters.";
        var at = email.IndexOf('@');
        if (at <= 0 || at != email.LastIndexOf('@') || email.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            return "Enter a valid email address, e.g. name@example.com.";
        var domain = email[(at + 1)..];
        var dot = domain.LastIndexOf('.');
        if (dot <= 0 || dot == domain.Length - 1 || domain.Contains("..") || domain.StartsWith('-'))
            return "Enter a valid email address, e.g. name@example.com.";
        return null;
    }
}
