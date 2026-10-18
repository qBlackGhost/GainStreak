using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

public record AuthResult(bool Success, string? Error = null, User? User = null, string? Field = null);

public class AuthService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<AuthResult> RegisterAsync(string username, string password)
    {
        username = username.Trim();
        if (Credentials.CheckUsername(username) is { } nameError) return new(false, nameError, Field: FormField.Username);
        if (Credentials.CheckPassword(password) is { } passwordError) return new(false, passwordError, Field: FormField.Password);

        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.Users.AnyAsync(u => u.Username == username))
            return new(false, "That username is already taken.", Field: FormField.Username);

        var user = new User { Username = username, PasswordHash = PasswordHasher.Hash(password) };
        db.Users.Add(user);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return new(false, "That username is already taken.", Field: FormField.Username); }

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