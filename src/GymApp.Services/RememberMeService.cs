using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GymApp.Data;
using GymApp.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

public record RememberedLogin(int UserId, string Token);

public class RememberMeService(IDbContextFactory<AppDbContext> dbFactory, SessionService session)
{
    private static string FilePath => Path.Combine(AppDbContext.DataDirectory, "remember.json");

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// Call after every successful login: saves a token if remember is true, clears it if false.
    public void Remember(User user, bool remember)
    {
        using var db = dbFactory.CreateDbContext();
        var u = db.Users.Find(user.Id);
        if (u is null) return;

        if (remember)
        {
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            u.RememberTokenHash = Hash(token);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new RememberedLogin(user.Id, token)));
        }
        else
        {
            u.RememberTokenHash = null;
            DeleteFile();
        }
        db.SaveChanges();
    }

    /// Call on logout.
    public void Forget()
    {
        if (session.CurrentUser is { } user) Remember(user, false);
        else DeleteFile();
    }

    /// Call once at startup, before the main view model is created.
    public bool TryRestore()
    {
        try
        {
            if (!File.Exists(FilePath)) return false;
            var saved = JsonSerializer.Deserialize<RememberedLogin>(File.ReadAllText(FilePath));
            if (saved is null) { DeleteFile(); return false; }

            using var db = dbFactory.CreateDbContext();
            var user = db.Users.Find(saved.UserId);

            var valid = user?.RememberTokenHash is { } stored &&
                CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(stored), Encoding.UTF8.GetBytes(Hash(saved.Token)));

            if (!valid) { DeleteFile(); return false; }

            session.SignIn(user!);
            return true;
        }
        catch (Exception)   // corrupt file, etc.: fall back to the normal login screen
        {
            DeleteFile();
            return false;
        }
    }

    private static void DeleteFile()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch (IOException) { }
    }
}