using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

public record AuthResult(bool Success, string? Error = null, User? User = null, string? Field = null);

public class AuthService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<AuthResult> RegisterAsync(string username, string email, string password)
    {
        username = username.Trim();
        email = Credentials.NormalizeEmail(email);
        if (Credentials.CheckUsername(username) is { } nameError) return new(false, nameError, Field: FormField.Username);
        if (Credentials.CheckEmail(email) is { } emailError) return new(false, emailError, Field: FormField.Email);
        if (Credentials.CheckPassword(password) is { } passwordError) return new(false, passwordError, Field: FormField.Password);

        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.Users.AnyAsync(u => u.Username == username))
            return new(false, "That username is already taken.", Field: FormField.Username);
        if (await db.Users.AnyAsync(u => u.Email == email))
            return new(false, "An account with that email already exists.", Field: FormField.Email);

        var user = new User { Username = username, Email = email, PasswordHash = PasswordHasher.Hash(password) };
        db.Users.Add(user);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return new(false, "That username or email is already taken."); }

        return new(true, User: user);
    }

    /// <summary>Signs in with the username or the email; anything with an '@' is looked up as an email.</summary>
    public async Task<AuthResult> LoginAsync(string usernameOrEmail, string password)
    {
        var login = usernameOrEmail.Trim();
        await using var db = await dbFactory.CreateDbContextAsync();
        User? user;
        if (login.Contains('@'))
        {
            var email = Credentials.NormalizeEmail(login);
            user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        }
        else
        {
            user = await db.Users.FirstOrDefaultAsync(u => u.Username == login);
        }

        if (user is null || !PasswordHasher.Verify(password, user.PasswordHash))
            return new(false, "Invalid username, email or password.");

        return new(true, User: user);
    }

    /// <summary>Sets or changes the account email. Asks for the current password, like any account change.</summary>
    public async Task<AuthResult> ChangeEmailAsync(int userId, string newEmail, string currentPassword)
    {
        var email = Credentials.NormalizeEmail(newEmail);
        if (Credentials.CheckEmail(email) is { } emailError) return new(false, emailError, Field: FormField.Email);

        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return new(false, "User not found.");
        if (!PasswordHasher.Verify(currentPassword, user.PasswordHash))
            return new(false, "That password is not right.", Field: FormField.Password);
        if (user.Email == email) return new(false, "That is already your email.", Field: FormField.Email);
        if (await db.Users.AnyAsync(u => u.Email == email && u.Id != userId))
            return new(false, "Another account already uses that email.", Field: FormField.Email);

        user.Email = email;
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return new(false, "Another account already uses that email.", Field: FormField.Email); }
        return new(true, User: user);
    }

    /// <summary>Changes the password after checking the current one.</summary>
    public async Task<AuthResult> ChangePasswordAsync(int userId, string currentPassword, string newPassword)
    {
        if (Credentials.CheckPassword(newPassword) is { } passwordError) return new(false, passwordError, Field: FormField.NewPassword);

        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return new(false, "User not found.");
        if (!PasswordHasher.Verify(currentPassword, user.PasswordHash))
            return new(false, "That password is not right.", Field: FormField.Password);

        user.PasswordHash = PasswordHasher.Hash(newPassword);
        await db.SaveChangesAsync();
        return new(true, User: user);
    }
}
