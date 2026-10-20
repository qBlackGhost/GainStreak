using System.Security.Claims;
using System.Text.Encodings.Web;
using GymApp.Server.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GymApp.Server.Auth;

/// <summary>Reads <c>Authorization: Bearer &lt;token&gt;</c>, finds the live session by the token's hash and slides its expiry.</summary>
public class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ServerDbContext db,
    TimeProvider clock)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Session";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();
        var token = header["Bearer ".Length..].Trim();
        if (token.Length == 0) return AuthenticateResult.Fail("No token.");

        var now = clock.GetUtcNow().UtcDateTime;
        var hash = SessionTokens.Hash(token);
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.TokenHash == hash, Context.RequestAborted);
        if (session is null || session.ExpiresAt <= now) return AuthenticateResult.Fail("Unknown or expired session.");

        if (now - session.LastUsedAt >= SessionTokens.SlideEvery)
        {
            await db.Sessions.Where(s => s.Id == session.Id).ExecuteUpdateAsync(u => u
                .SetProperty(s => s.LastUsedAt, now)
                .SetProperty(s => s.ExpiresAt, now + SessionTokens.Lifetime), Context.RequestAborted);
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, session.UserId.ToString()),
                new Claim(SessionTokens.SessionIdClaim, session.Id.ToString())
            ], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
