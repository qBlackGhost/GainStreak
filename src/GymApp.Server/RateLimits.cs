using System.Net;
using System.Net.Sockets;
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
            // Attempts, failed ones included: a cap on the work a script can cause. Accounts actually created are
            // limited separately and more tightly (SignUpLimiter), so mistakes behind a shared address don't count
            o.AddPolicy(Register, ctx => PerIp(ctx, RegisterAttemptsPerHour, TimeSpan.FromHours(1)));
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

    /// <summary>Sign-up attempts per address an hour, failed ones included.</summary>
    public const int RegisterAttemptsPerHour = 60;

    /// <summary>The partition for a client address: an IPv4 address as is (also when mapped into IPv6), an IPv6
    /// address by its /64 network. A phone or home gets a whole /64, so limiting single IPv6 addresses would let one
    /// device rotate through billions of them.</summary>
    public static string AddressKey(IPAddress? address)
    {
        if (address is null) return "ip:";
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetworkV6) return $"ip:{address}";
        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return $"ip:{new IPAddress(bytes)}/64";
    }

    private static RateLimitPartition<string> PerIp(HttpContext ctx, int permits, TimeSpan window) =>
        Fixed(AddressKey(ctx.Connection.RemoteIpAddress), permits, window);

    private static string UserOrIp(HttpContext ctx) =>
        ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } id ? $"user:{id}" : AddressKey(ctx.Connection.RemoteIpAddress);

    private static RateLimitPartition<string> Fixed(string key, int permits, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = window,
            QueueLimit = 0
        });
}
