using System.Threading.Tasks;
using GymApp.Client;
using GymApp.Contracts;
using GymApp.Data.Models;

namespace GymApp.Services;

/// <summary>How many users train with each split, for the split pages' usage chart. Null when there's nothing to show.</summary>
public interface ISplitUsageService
{
    Task<SplitUsageResponse?> GetAsync(int userId);
}

/// <summary>From the social server, which counts every account's active split. Offline it answers from the last
/// fetch, or null if there was none.</summary>
public class RemoteSplitUsageService(SocialApiClient api, SocialCacheStore cache, SessionService session) : ISplitUsageService
{
    public async Task<SplitUsageResponse?> GetAsync(int userId)
    {
        SignedInGuard.Check(session, userId);
        var r = await api.GetSplitUsageAsync();
        if (r.IsOk)
        {
            await cache.SaveAsync(userId, SocialCacheKeys.SplitUsage, r.Value!);
            return r.Value;
        }
        return (await cache.GetAsync<SplitUsageResponse>(userId, SocialCacheKeys.SplitUsage))?.Value;
    }
}

/// <summary>The TestBench's local mode has no server counting splits.</summary>
public class LocalSplitUsageService : ISplitUsageService
{
    public Task<SplitUsageResponse?> GetAsync(int userId) => Task.FromResult<SplitUsageResponse?>(null);
}
