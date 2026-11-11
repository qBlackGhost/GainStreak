using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace GymApp.Server;

/// <summary>Rate limit policies. Register, login and search are limited per IP (they're what a script would hammer),
/// feedback, reports and sharing per user; everything else per signed-in user, or per IP before sign-in.</summary>
public static class RateLimits
{
    public const string Register = "register";
    public const string Login = "login";
    public const string Search = "search";
    public const string Feedback = "feedback";
    public const string Share = "share";
    public const string Report = "report";
    public const string EmailCode = "email-code";
    public const string PasswordReset = "password-reset";
    public const string Invite = "invite";
    public const string InviteReset = "invite-reset";

    public static IServiceCollection AddGymRateLimits(this IServiceCollection services) =>
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(Register, ctx => PerIp(ctx, 5, TimeSpan.FromHours(1)));
            o.AddPolicy(Login, ctx => PerIp(ctx, 10, TimeSpan.FromMinutes(1)));
            o.AddPolicy(Search, ctx => PerIp(ctx, 30, TimeSpan.FromMinutes(1)));
            // Per signed-in user: enough for a tester on a bad day, not for a script filling the Discord channel
            o.AddPolicy(Feedback, ctx => Fixed($"feedback:{UserOrIp(ctx)}", 5, TimeSpan.FromHours(1)));
            o.AddPolicy(Share, ctx => Fixed($"share:{UserOrIp(ctx)}", 30, TimeSpan.FromHours(1)));
            // Each sends an email, and a wrong code costs a try: both are what a script would hammer
            o.AddPolicy(EmailCode, ctx => Fixed($"email-code:{UserOrIp(ctx)}", 10, TimeSpan.FromHours(1)));
            o.AddPolicy(PasswordReset, ctx => PerIp(ctx, 10, TimeSpan.FromHours(1)));
            o.AddPolicy(Report, ctx => Fixed($"report:{UserOrIp(ctx)}", 10, TimeSpan.FromHours(1)));
            // Looking up and accepting invite codes: a script trying codes is what this stops
            o.AddPolicy(Invite, ctx => Fixed($"invite:{UserOrIp(ctx)}", 30, TimeSpan.FromMinutes(1)));
            o.AddPolicy(InviteReset, ctx => Fixed($"invite-reset:{UserOrIp(ctx)}", 10, TimeSpan.FromHours(1)));
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                Fixed(UserOrIp(ctx), 120, TimeSpan.FromMinutes(1)));
        });

    private static RateLimitPartition<string> PerIp(HttpContext ctx, int permits, TimeSpan window) =>
        Fixed($"ip:{ctx.Connection.RemoteIpAddress}", permits, window);

    private static string UserOrIp(HttpContext ctx) =>
        ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } id ? $"user:{id}" : $"ip:{ctx.Connection.RemoteIpAddress}";

    private static RateLimitPartition<string> Fixed(string key, int permits, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = window,
            QueueLimit = 0
        });
}
