namespace GymApp.Core;

/// <summary>The letters on an avatar without a picture (0.23.1). Usernames have no spaces, so '_', '.' and '-' split
/// the words: one word gives its first letter ("cristi" → "C"), two or more give the first letter of the first two
/// ("andrei.m" → "AM", "vlad_t_99" → "VT"). A word with no letter doesn't count ("john_99" → "J"); a username with no
/// letter at all shows its first character ("12345" → "1"), and an empty one "?".</summary>
public static class UserInitials
{
    private static readonly char[] Separators = ['_', '.', '-'];

    public static string Of(string? username)
    {
        if (string.IsNullOrWhiteSpace(username)) return "?";
        var letters = username.Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.FirstOrDefault(char.IsLetter))
            .Where(c => c != default)
            .Take(2)
            .ToArray();
        return letters.Length > 0
            ? new string(letters).ToUpperInvariant()
            : username.Trim()[..1].ToUpperInvariant();
    }
}
