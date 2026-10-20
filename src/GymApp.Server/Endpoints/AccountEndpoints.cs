using System.Security.Claims;
using GymApp.Contracts;
using GymApp.Core;
using GymApp.Server.Auth;
using GymApp.Server.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GymApp.Server.Endpoints;

/// <summary><c>/v1/auth</c> (register, login, logout) and <c>/v1/me</c> (the account itself).</summary>
public static class AccountEndpoints
{
    private const string UsernameTaken = "That username is taken.";
    private const string EmailUsed = "That email is already used.";
    private const string WrongLogin = "Wrong username or password.";
    private const string WrongPassword = "That password is not right.";
    private const string SameEmail = "That is already your email.";

    /// <summary>Verified against when the login matches no user, so an unknown name costs as much time as a wrong password.</summary>
    private static readonly Lazy<string> DummyHash = new(() => PasswordHasher.Hash(Guid.NewGuid().ToString()));

    public static void MapAccounts(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/v1/auth");
        auth.MapPost("/register", Register).AllowAnonymous().RequireRateLimiting(RateLimits.Register);
        auth.MapPost("/login", Login).AllowAnonymous().RequireRateLimiting(RateLimits.Login);
        auth.MapPost("/logout", Logout).RequireAuthorization();

        var me = app.MapGroup("/v1/me");
        me.MapGet("", GetMe);
        me.MapPut("/email", ChangeEmail);
        me.MapPut("/password", ChangePassword);
        me.MapDelete("", DeleteAccount);
    }

    private static async Task<IResult> Register(RegisterRequest request, ServerDbContext db, TimeProvider clock)
    {
        var username = (request.Username ?? "").Trim();
        var email = Credentials.NormalizeEmail(request.Email ?? "");
        var password = request.Password ?? "";
        if (Credentials.CheckUsername(username) is { } nameError) return Problems.BadRequest(nameError, ProblemFields.Username);
        if (Credentials.CheckEmail(email) is { } emailError) return Problems.BadRequest(emailError, ProblemFields.Email);
        if (Credentials.CheckPassword(password) is { } passwordError) return Problems.BadRequest(passwordError, ProblemFields.Password);

        var normalized = User.NormalizeUsername(username);
        if (await db.Users.AnyAsync(u => u.UsernameNormalized == normalized)) return Problems.Conflict(UsernameTaken, ProblemFields.Username);
        if (await db.Users.AnyAsync(u => u.Email == email)) return Problems.Conflict(EmailUsed, ProblemFields.Email);

        var now = clock.GetUtcNow().UtcDateTime;
        var user = new User
        {
            Username = username,
            UsernameNormalized = normalized,
            Email = email,
            PasswordHash = PasswordHasher.Hash(password),
            CreatedAt = now
        };
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            // Someone registered the same name or email between the check and the insert
            return pg.ConstraintName == ServerDbContext.EmailIndex
                ? Problems.Conflict(EmailUsed, ProblemFields.Email)
                : Problems.Conflict(UsernameTaken, ProblemFields.Username);
        }

        var token = SessionTokens.Start(db, user.Id, request.DeviceName, now);
        await db.SaveChangesAsync();
        return Results.Created("/v1/me", new AuthResponse(token, ToMe(user)));
    }

    private static async Task<IResult> Login(LoginRequest request, ServerDbContext db, TimeProvider clock)
    {
        var login = (request.Login ?? "").Trim();
        var password = request.Password ?? "";
        // No real password is this long, and hashing a huge one is wasted work; the length says nothing about the user
        if (password.Length > Credentials.MaxPasswordLength) return Problems.Unauthorized(WrongLogin);

        User? user;
        if (login.Contains('@'))
        {
            var email = Credentials.NormalizeEmail(login);
            user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        }
        else
        {
            var normalized = User.NormalizeUsername(login);
            user = await db.Users.FirstOrDefaultAsync(u => u.UsernameNormalized == normalized);
        }

        // Always hash, so the response time doesn't tell which usernames exist
        var valid = PasswordHasher.Verify(password, user?.PasswordHash ?? DummyHash.Value) && user is not null;
        if (!valid) return Problems.Unauthorized(WrongLogin);

        var token = SessionTokens.Start(db, user!.Id, request.DeviceName, clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync();
        return Results.Ok(new AuthResponse(token, ToMe(user)));
    }

    private static async Task<IResult> Logout(ClaimsPrincipal principal, ServerDbContext db)
    {
        var sessionId = principal.SessionId();
        await db.Sessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> GetMe(ClaimsPrincipal principal, ServerDbContext db)
    {
        var userId = principal.UserId();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        return user is null ? Results.NotFound() : Results.Ok(ToMe(user));
    }

    private static async Task<IResult> ChangeEmail(ChangeEmailRequest request, ClaimsPrincipal principal, ServerDbContext db)
    {
        var email = Credentials.NormalizeEmail(request.Email ?? "");
        if (Credentials.CheckEmail(email) is { } emailError) return Problems.BadRequest(emailError, ProblemFields.Email);

        var userId = principal.UserId();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return Results.NotFound();
        if (!PasswordHasher.Verify(request.CurrentPassword ?? "", user.PasswordHash))
            return Problems.BadRequest(WrongPassword, ProblemFields.Password);
        if (user.Email == email) return Problems.BadRequest(SameEmail, ProblemFields.Email);
        if (await db.Users.AnyAsync(u => u.Email == email)) return Problems.Conflict(EmailUsed, ProblemFields.Email);

        user.Email = email;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Someone took the email between the check and the update
            return Problems.Conflict(EmailUsed, ProblemFields.Email);
        }
        return Results.NoContent();
    }

    private static async Task<IResult> ChangePassword(ChangePasswordRequest request, ClaimsPrincipal principal, ServerDbContext db)
    {
        var userId = principal.UserId();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return Results.NotFound();
        if (!PasswordHasher.Verify(request.CurrentPassword ?? "", user.PasswordHash))
            return Problems.BadRequest(WrongPassword, ProblemFields.Password);
        var newPassword = request.NewPassword ?? "";
        if (Credentials.CheckPassword(newPassword) is { } passwordError) return Problems.BadRequest(passwordError, ProblemFields.NewPassword);

        // Every other device has to sign in again with the new password; this one stays signed in
        var sessionId = principal.SessionId();
        await using var tx = await db.Database.BeginTransactionAsync();
        user.PasswordHash = PasswordHasher.Hash(newPassword);
        await db.SaveChangesAsync();
        await db.Sessions.Where(s => s.UserId == userId && s.Id != sessionId).ExecuteDeleteAsync();
        await tx.CommitAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAccount([FromBody] DeleteAccountRequest request, ClaimsPrincipal principal, ServerDbContext db)
    {
        var userId = principal.UserId();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return Results.NotFound();
        if (!PasswordHasher.Verify(request.Password ?? "", user.PasswordHash))
            return Problems.BadRequest(WrongPassword, ProblemFields.Password);

        // The database cascades to sessions and, from 13e on, friendships, stats and check-ins
        await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync();
        return Results.NoContent();
    }

    private static MeResponse ToMe(User user) => new(user.Id, user.Username, user.Email, user.ShareGymCheckIns);
}
