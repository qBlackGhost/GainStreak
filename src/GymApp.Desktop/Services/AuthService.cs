using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Desktop.Data;
using GymApp.Desktop.Models;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Desktop.Services;

public record AuthResult(bool Success, string? Error = null, User? User = null);

public class AuthService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<AuthResult> RegisterAsync(string username, string password)
    {
        username = username.Trim();
        if (username.Length < 3) return new(false, "Username must be at least 3 characters.");
        if (username.Length > 32) return new(false, "Username must be at most 32 characters.");
        if (password.Length < 8) return new(false, "Password must be at least 8 characters.");

        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.Users.AnyAsync(u => u.Username == username))
            return new(false, "That username is already taken.");

        var user = new User { Username = username, PasswordHash = PasswordHasher.Hash(password) };
        db.Users.Add(user);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return new(false, "That username is already taken."); }

        return new(true, User: user);
    }

    public async Task<AuthResult> LoginAsync(string username, string password)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username.Trim());

        if (user is null || !PasswordHasher.Verify(password, user.PasswordHash))
            return new(false, "Invalid username or password.");

        return new(true, User: user);
    }
}