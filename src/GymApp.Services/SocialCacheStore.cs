using System;
using System.Text.Json;
using System.Threading.Tasks;
using GymApp.Contracts;
using GymApp.Data;
using GymApp.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

/// <summary>The last successful server answer per user and <see cref="SocialCacheKeys">key</see>, kept as the API's
/// JSON so the cached value maps back exactly like a fresh one.</summary>
public class SocialCacheStore(IDbContextFactory<AppDbContext> dbFactory, TimeProvider? clock = null)
{
    public DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    public async Task SaveAsync<T>(int userId, string key, T value)
    {
        var json = JsonSerializer.Serialize(value, ApiFormat.Json);
        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await db.SocialCache.FirstOrDefaultAsync(c => c.UserId == userId && c.Key == key);
        if (row is null)
        {
            row = new SocialCache { UserId = userId, Key = key };
            db.SocialCache.Add(row);
        }
        row.Json = json;
        row.FetchedAt = Now;
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { /* another read saved the same key at the same moment; either copy is fine */ }
    }

    /// <summary>The cached value and when it was fetched, or null if there is none (or it no longer reads).</summary>
    public async Task<(T Value, DateTime FetchedAt)?> GetAsync<T>(int userId, string key)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await db.SocialCache.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == userId && c.Key == key);
        if (row is null) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(row.Json, ApiFormat.Json) is { } value ? (value, row.FetchedAt) : null;
        }
        catch (JsonException)
        {
            return null;   // written by an older version: treat as no cache
        }
    }
}
