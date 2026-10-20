using System.Security.Claims;
using GymApp.Contracts;
using GymApp.Server.Auth;
using GymApp.Server.Data;
using GymApp.Server.Feedback;

namespace GymApp.Server.Endpoints;

/// <summary><c>POST /v1/feedback</c>: a problem report or an idea from the app's Feedback popup. Stored, then
/// forwarded to Discord in the background when a webhook is set (<see cref="DiscordFeedbackForwarder"/>).</summary>
public static class FeedbackEndpoints
{
    public const string TextField = "text";

    public static void MapFeedback(this IEndpointRouteBuilder app) =>
        app.MapPost("/v1/feedback", Send).RequireRateLimiting(RateLimits.Feedback);

    private static async Task<IResult> Send(FeedbackRequest request, HttpContext http, ClaimsPrincipal me,
        ServerDbContext db, FeedbackQueue queue, TimeProvider clock)
    {
        // Scrubbed again (an old or other client may not have), before the length checks: "<user>" can be longer
        var text = FeedbackRules.Scrub((request.Text ?? "").Trim());
        if (text.Length == 0) return Problems.BadRequest(FeedbackRules.EmptyMessage, TextField);
        if (text.Length > FeedbackRules.MaxTextLength) return Problems.BadRequest(FeedbackRules.TooLongMessage, TextField);
        var report = string.IsNullOrWhiteSpace(request.Report) ? null : FeedbackRules.Scrub(request.Report);
        if (report is not null && request.Kind != FeedbackKind.Problem) return Problems.BadRequest(FeedbackRules.ReportOnIdeaMessage);
        if (report is { Length: > FeedbackRules.MaxReportLength }) return Problems.BadRequest(FeedbackRules.ReportTooLongMessage);

        var version = http.Request.Headers[ClientVersions.Header].ToString();
        var entry = new FeedbackEntry
        {
            UserId = me.UserId(),
            Kind = request.Kind,
            Text = text,
            Report = report,
            AppVersion = version.Length == 0 ? null : version[..Math.Min(version.Length, 32)],
            CreatedAt = clock.GetUtcNow().UtcDateTime
        };
        db.Feedback.Add(entry);
        await db.SaveChangesAsync();
        queue.Enqueue(entry.Id);
        return Results.NoContent();
    }
}
