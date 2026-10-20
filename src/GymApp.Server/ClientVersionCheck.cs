using GymApp.Contracts;

namespace GymApp.Server;

/// <summary>
/// Turns away app builds older than <c>MinClientVersion</c> (env <c>MinClientVersion</c>, e.g. "0.15.0") with a 426
/// and a message the app shows as "please update". Unset, every build is accepted. Only the API (/v1) is checked, not
/// /health. A request with no version header counts as too old: builds from before 0.15 don't send one.
/// </summary>
public static class ClientVersionCheck
{
    public const string SettingName = "MinClientVersion";
    public const string TooOldMessage = "This version of GainStreak is too old. Update the app to keep using friends and the leaderboard.";

    /// <summary>The configured minimum, null when unset. Throws for a value that isn't a version, so a typo stops
    /// startup instead of letting every build in.</summary>
    public static Version? ReadMinimum(IConfiguration config)
    {
        var text = config[SettingName];
        if (string.IsNullOrWhiteSpace(text)) return null;
        return ClientVersions.Parse(text)
            ?? throw new InvalidOperationException($"{SettingName} must be a version like 0.15.0 (it is \"{text}\").");
    }

    public static bool IsAccepted(string? sent, Version minimum) => ClientVersions.Parse(sent) is { } v && v >= minimum;

    public static IApplicationBuilder UseClientVersionCheck(this IApplicationBuilder app, Version? minimum)
    {
        if (minimum is null) return app;
        var minText = minimum.ToString(3);
        return app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/v1")
                && !IsAccepted(context.Request.Headers[ClientVersions.Header], minimum))
            {
                await Results.Problem(detail: TooOldMessage, statusCode: StatusCodes.Status426UpgradeRequired,
                        extensions: new Dictionary<string, object?> { [ClientVersions.MinVersionField] = minText })
                    .ExecuteAsync(context);
                return;
            }
            await next(context);
        });
    }
}
