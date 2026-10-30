namespace GymApp.Core;

/// <summary>A user's avatar as it's stored and sent: a colour, and optionally an icon in place of the initials
/// ("violet" or "violet:flame"). Null everywhere means the plain initials.</summary>
public sealed record AvatarChoice(string Color, string? Icon)
{
    public const int MaxLength = 32;

    /// <summary>The choices, in the order the picker shows them. Never rename one in use. A new one needs the server
    /// deployed first: it refuses a choice it doesn't know.</summary>
    public static readonly IReadOnlyList<string> Colors = ["black", "red", "orange", "green", "teal", "blue", "violet", "pink", "slate"];
    public static readonly IReadOnlyList<string> Icons = ["dumbbell", "flame", "trophy", "star", "bolt", "heart", "mountain", "target"];

    public override string ToString() => Icon is null ? Color : $"{Color}:{Icon}";

    /// <summary>The choice in <paramref name="text"/>, or null for none or one this build doesn't know.</summary>
    public static AvatarChoice? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var parts = text.Split(':');
        if (parts.Length > 2 || !Colors.Contains(parts[0])) return null;
        if (parts.Length == 1) return new(parts[0], null);
        return Icons.Contains(parts[1]) ? new(parts[0], parts[1]) : null;
    }
}

/// <summary>The ring around an avatar, which comes from the level alone: it can't be chosen.</summary>
public enum AvatarRing { Starter, Silver, Lime, Gold }

public static class AvatarRings
{
    public static AvatarRing For(int level) => level switch
    {
        >= 7 => AvatarRing.Gold,
        >= 5 => AvatarRing.Lime,
        >= 3 => AvatarRing.Silver,
        _ => AvatarRing.Starter,
    };
}
