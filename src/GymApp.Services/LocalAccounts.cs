using System.Linq;
using System.Threading.Tasks;
using GymApp.Data;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

/// <summary>Account housekeeping on the local database, shared by both account services.</summary>
public static class LocalAccounts
{
    /// <summary>Deletes a user and everything that cascades from them. Workout logs go first: a log may point at the
    /// user's own custom gym, and gyms are Restrict for logs, so the cascade alone could stop halfway.</summary>
    public static async Task DeleteUserAsync(AppDbContext db, int userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.WorkoutLogs.Where(w => w.UserId == userId).ExecuteDeleteAsync();
        await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync();
        await tx.CommitAsync();
    }
}
