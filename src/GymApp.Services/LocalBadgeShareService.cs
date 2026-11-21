using System.Linq;
using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using GymApp.Social;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

/// <summary>Badges between friends in the TestBench's local mode (Phase 25): every account lives in this database, so
/// a friend's badges are read straight from it. Nobody joins through an invite here, so Recruiter stays at 0.</summary>
public class LocalBadgeShareService(IDbContextFactory<AppDbContext> dbFactory) : IBadgeShareService
{
    public async Task<FriendBadges?> GetFriendBadgesAsync(int userId, int friendId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var (a, b) = userId < friendId ? (userId, friendId) : (friendId, userId);
        if (!await db.Friendships.AnyAsync(f => f.UserAId == a && f.UserBId == b && f.Status == FriendshipStatus.Accepted)) return null;
        var settings = await new BadgeService(dbFactory).GetSettingsAsync(friendId);
        if (!settings.Share) return new FriendBadges(friendId, [], null);
        var earned = await db.EarnedBadges.AsNoTracking().Where(e => e.UserId == friendId)
            .OrderByDescending(e => e.EarnedOn).ThenBy(e => e.BadgeId).ToListAsync();
        return new FriendBadges(friendId, earned.Select(e => new SharedBadge(e.BadgeId, e.Tier, e.EarnedOn)).ToList(), settings.FeaturedId);
    }

    public Task<int?> GetAcceptedInvitesAsync(int userId) => Task.FromResult<int?>(0);
}
