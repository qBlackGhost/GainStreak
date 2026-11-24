using System.Text.RegularExpressions;

namespace GymApp.Core;

/// <summary>Rules for gyms a user adds themselves (gyms OpenStreetMap doesn't have).</summary>
public static partial class CustomGyms
{
    public const int MaxPerUser = 10;          // active ones; deleted-but-kept-for-history gyms don't count
    public const int MaxNameLength = 40;      // 0.27.1: was 60; long names wrapped onto 3 or 4 lines on phones
    public const int MaxAddressLength = 120;

    /// <summary>Custom gyms need a picked address (18c), so workouts there can be verified.</summary>
    public const string AddressRequired = "Pick the gym's address so workouts there can be verified.";

    /// <summary>Trims and collapses runs of spaces, so "  Iron   Gym " and "Iron Gym" are the same name.</summary>
    public static string Clean(string? text) => Spaces().Replace(text ?? "", " ").Trim();

    /// <summary>The cleaned address, or null when it is empty.</summary>
    public static string? CleanAddress(string? address) => Clean(address) is { Length: > 0 } a ? a : null;

    /// <summary>Null when the cleaned name is fine, otherwise the message to show.</summary>
    public static string? CheckName(string cleanName) =>
        cleanName.Length == 0 ? "Give your gym a name."
        : cleanName.Length > MaxNameLength ? $"Keep the name to {MaxNameLength} characters or fewer."
        : null;

    public static string? CheckAddress(string? cleanAddress) =>
        cleanAddress is { Length: > MaxAddressLength } ? $"Keep the address under {MaxAddressLength + 1} characters." : null;

    /// <summary>Names match ignoring case, including letters like Ș/ș that SQLite's NOCASE doesn't fold.</summary>
    public static bool SameName(string a, string b) => string.Equals(Clean(a), Clean(b), System.StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
