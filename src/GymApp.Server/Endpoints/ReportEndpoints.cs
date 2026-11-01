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

        db.UserReports.Add(new UserReport
        {
            ReporterId = reporterId,
            ReportedUserId = userId,
            Reason = request.Reason,
            Note = note,
            CreatedAt = clock.GetUtcNow().UtcDateTime
        });
        await db.SaveChangesAsync();
        // Not the request's token: the row is saved, so the post should finish even if the phone stops waiting
        if (http.RequestServices.GetService<DiscordFeedbackForwarder>() is { } forwarder)
            await forwarder.ForwardPendingReportsAsync();
        return Results.NoContent();
    }
}
