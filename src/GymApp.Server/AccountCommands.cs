using GymApp.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Server;

/// <summary>
/// <c>dotnet GymApp.Server.dll suspend &lt;username&gt;</c> and <c>unsuspend &lt;username&gt;</c>, for acting on reports.
/// Commands like <c>migrate</c> rather than an endpoint: nothing on the public API can suspend anyone, and running one
/// takes the database's credentials. Suspending ends every session, so the app signs out at its next request.
/// </summary>
public static class AccountCommands
{
    public static bool IsCommand(string[] args) => args is ["suspend" or "unsuspend", _];

    /// <summary>What happened, in a line; <c>Ok</c> is false for a username that doesn't exist.</summary>
    public static async Task<(bool Ok, string Message)> RunAsync(string[] args, ServerDbContext db, TimeProvider clock)
    {
        var suspend = args[0] == "suspend";
        var normalized = User.NormalizeUsername(args[1]);
        var user = await db.Users.FirstOrDefaultAsync(u => u.UsernameNormalized == normalized);
        if (user is null) return (false, $"No user is called {args[1]}.");

        await using var tx = await db.Database.BeginTransactionAsync();
        user.SuspendedAt = suspend ? user.SuspendedAt ?? clock.GetUtcNow().UtcDateTime : null;
        await db.SaveChangesAsync();
        if (suspend) await db.Sessions.Where(s => s.UserId == user.Id).ExecuteDeleteAsync();
        await tx.CommitAsync();
        return (true, $"{user.Username} (user {user.Id}) is {(suspend ? "suspended" : "no longer suspended")}.");
    }
}
