using System.Security.Claims;
using GymApp.Contracts;
using GymApp.Server.Auth;
using GymApp.Server.Data;
using GymApp.Server.Feedback;
using GymApp.Social;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Server.Endpoints;

/// <summary><c>POST /v1/users/{userId}/report</c>: stored, then forwarded to Discord before answering when a webhook is
/// set, like feedback. Anyone can be reported, friend or not: a username in a search is enough to offend. The reported
/// user isn't told.</summary>
public static class ReportEndpoints
{
    public static void MapReports(this IEndpointRouteBuilder app) =>
        app.MapPost("/v1/users/{userId:int}/report", Send).RequireRateLimiting(RateLimits.Report);

    private static async Task<IResult> Send(int userId, UserReportRequest request, HttpContext http, ClaimsPrincipal me,
        ServerDbContext db, TimeProvider clock)
    {
        var reporterId = me.UserId();
        if (userId == reporterId) return Problems.BadRequest(UserReportRules.SelfMessage);
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note is { Length: > UserReportRules.MaxNoteLength }) return Problems.BadRequest(UserReportRules.TooLongMessage);
        if (!await db.Users.AnyAsync(u => u.Id == userId)) return Problems.NotFound(FriendService.UnknownUserError);

        // 10 minutes between reports (0.28.1), whoever they're about; the hourly rate limit stays as a backstop
        var now = clock.GetUtcNow().UtcDateTime;
        var last = await db.UserReports.Where(r => r.ReporterId == reporterId).MaxAsync(r => (DateTime?)r.CreatedAt);
        if (last is { } l && now < l + UserReportRules.Cooldown)
            return Problems.TooManyRequests(UserReportRules.TooSoonMessage(l + UserReportRules.Cooldown - now),
                extensions: new Dictionary<string, object?> { [ProblemExtensions.RetryAt] = l + UserReportRules.Cooldown });

        db.UserReports.Add(new UserReport
        {
            ReporterId = reporterId,
            ReportedUserId = userId,
            Reason = request.Reason,
            Note = note,
            CreatedAt = now
        });
        await db.SaveChangesAsync();
        // Not the request's token: the row is saved, so the post should finish even if the phone stops waiting
        if (http.RequestServices.GetService<DiscordFeedbackForwarder>() is { } forwarder)
            await forwarder.ForwardPendingReportsAsync();
        return Results.NoContent();
    }
}
