using System.Security.Cryptography;
using System.Text;
using GymApp.Contracts;
using GymApp.Server.Auth;
using GymApp.Server.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GymApp.Server.Email;

/// <summary>The codes that confirm an email or reset a password: six digits, good for <see cref="EmailCode.Lifetime"/>
/// and <see cref="EmailCode.MaxAttempts"/> tries. A code proves the person reads that inbox, so the hash also covers the
/// user: a code can't be looked up across accounts.</summary>
public class EmailCodeService(ServerDbContext db, IEmailSender sender, TimeProvider clock)
{
    public enum Outcome { Sent, TooSoon, NotSent }

    /// <summary>Replaces any code of that purpose and emails the new one. If it doesn't go out the code is dropped, so the
    /// wait for another one doesn't start. Two requests at once: the second counts as too soon.</summary>
    public async Task<Outcome> SendAsync(User user, EmailCodePurpose purpose)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var row = await db.EmailCodes.FirstOrDefaultAsync(c => c.UserId == user.Id && c.Purpose == purpose);
        if (row is not null && now - row.CreatedAt < EmailCode.ResendAfter) return Outcome.TooSoon;

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D" + EmailCodeRules.Length);
        if (row is null)
        {
            row = new EmailCode { UserId = user.Id, Purpose = purpose };
            db.EmailCodes.Add(row);
        }
        row.CodeHash = Hash(user.Id, code);
        row.Attempts = 0;
        row.CreatedAt = now;
        row.ExpiresAt = now + EmailCode.Lifetime;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(row).State = EntityState.Detached;
            return Outcome.TooSoon;
        }

        var (subject, intro) = purpose == EmailCodePurpose.ConfirmEmail
            ? ("Confirm your GainStreak email", "Use this code to confirm your email:")
            : ("Reset your GainStreak password", "Use this code to reset your password:");
        var minutes = (int)EmailCode.Lifetime.TotalMinutes;
        var body = $"Hi {user.Username},\n\n{intro}\n\n{code}\n\nIt works for {minutes} minutes. " +
                   "If this wasn't you, you can ignore this email.\n\nGainStreak";
        if (await sender.SendAsync(user.Email, subject, body)) return Outcome.Sent;
        db.EmailCodes.Remove(row);
        await db.SaveChangesAsync();
        return Outcome.NotSent;
    }

    /// <summary>Whether the code is right. A right one is used up; a wrong one counts as a try.</summary>
    public async Task<bool> CheckAsync(int userId, EmailCodePurpose purpose, string? code)
    {
        var row = await db.EmailCodes.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == userId && c.Purpose == purpose);
        if (row is null) return false;
        if (row.Attempts >= EmailCode.MaxAttempts || row.ExpiresAt <= clock.GetUtcNow().UtcDateTime) return false;

        // The try is counted before the code is compared, in one statement: requests sent at the same moment can't each
        // read the same count and get more tries than MaxAttempts between them
        var counted = await db.EmailCodes.Where(c => c.Id == row.Id && c.Attempts < EmailCode.MaxAttempts)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.Attempts, c => c.Attempts + 1));
        if (counted == 0) return false;

        var given = Encoding.UTF8.GetBytes(Hash(userId, (code ?? "").Trim()));
        if (!CryptographicOperations.FixedTimeEquals(given, Encoding.UTF8.GetBytes(row.CodeHash))) return false;
        // Used up; if another request with the right code got here first, only that one counts
        return await db.EmailCodes.Where(c => c.Id == row.Id).ExecuteDeleteAsync() == 1;
    }

    private static string Hash(int userId, string code) => SessionTokens.Hash($"{userId}:{code}");
}
