using System;
using System.Net.Http;

namespace GymApp.Services;

/// <summary>Shared setup for the web APIs the app calls itself (OpenStreetMap, Open Food Facts): a User-Agent that names the
/// app. <see cref="OsmHttp"/> forwards here.</summary>
public static class AppHttp
{
    /// <summary>The project's public site, which Open Food Facts asks to find in the User-Agent in place of a contact
    /// address. Never the user's email.</summary>
    public const string Site = "https://gainstreak.app";

    /// <summary>"GainStreak/0.15.6 (Android; student project)": the app sets it at startup (<see cref="ForPlatform"/>),
    /// before any client is made.</summary>
    public static string UserAgent { get; set; } = "GainStreak/1.0 (student project)";

    /// <summary>The User-Agent naming the app's version and the platform it runs on (16f).</summary>
    public static string ForPlatform(string version, string platform) => $"GainStreak/{version} ({platform}; student project)";

    /// <summary>"AppName/Version (platform; student project; site)", the form Open Food Facts asks for.</summary>
    public static string WithSite(string userAgent) =>
        userAgent.EndsWith(')') ? $"{userAgent[..^1]}; {Site})" : $"{userAgent} ({Site})";

    public static HttpClient CreateClient(TimeSpan timeout, HttpMessageHandler? handler = null, string? userAgent = null)
    {
        var client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        client.Timeout = timeout;
        client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent ?? UserAgent);
        return client;
    }
}
