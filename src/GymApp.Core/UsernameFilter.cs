using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GymApp.Core;

/// <summary>
/// Keeps slurs, profanity and names that pass as the app's staff out of new usernames. A first filter, not a judge:
/// what gets past it is what Report is for. Names are compared lowercase, with '_', '.' and '-' removed and digits read
/// as the letters they stand in for, so "f_u.c-k" and "sh1t" don't slip through.
/// </summary>
public static class UsernameFilter
{
    /// <summary>Refused anywhere in a name. Only words that don't turn up inside ordinary names belong here.</summary>
    private static readonly string[] Anywhere =
    [
        "asshole", "bitch", "fuck", "faggot", "nigg", "penis", "pussy", "retard", "shit", "slut", "tranny", "whore",
        "hitler", "gainstreak",
        // Romanian
        "pizda"
    ];

    /// <summary>Refused only as the whole name or one part of it ("rape", "my.rape"): these hide inside real words and
    /// names (grape, Hancock, Scunthorpe, popular, torpedo).</summary>
    private static readonly HashSet<string> WholeWord = new(StringComparer.Ordinal)
    {
        "ass", "chink", "cock", "coon", "cum", "cunt", "dick", "fag", "kike", "kys", "nazi", "pedo", "rape", "spic",
        "admin", "administrator", "moderator", "mod", "official", "staff", "support", "system",
        // Romanian
        "curva", "futut", "muie", "muist", "pula"
    };

    public static bool IsAllowed(string username)
    {
        var lower = username.ToLowerInvariant();
        var whole = Read(lower.Where(c => c is not ('_' or '.' or '-')));
        if (Forms(whole).Any(form => Anywhere.Any(form.Contains))) return false;
        if (Forms(whole).Any(WholeWord.Contains)) return false;
        return !lower.Split('_', '.', '-').Any(part => Forms(Read(part)).Any(WholeWord.Contains));
    }

    /// <summary>The name, and the name with repeated letters squeezed ("fuuuck" is "fuck").</summary>
    private static IEnumerable<string> Forms(string name)
    {
        yield return name;
        var squeezed = new StringBuilder();
        foreach (var c in name)
            if (squeezed.Length == 0 || squeezed[^1] != c) squeezed.Append(c);
        yield return squeezed.ToString();
    }

    private static string Read(IEnumerable<char> name) => new(name.Select(c => c switch
    {
        '0' => 'o', '1' => 'i', '3' => 'e', '4' => 'a', '5' => 's', '7' => 't', _ => c
    }).ToArray());
}
