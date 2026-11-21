using System.Globalization;
using System.Security.Claims;
using GymApp.Contracts;
using GymApp.Core;
using GymApp.Server.Auth;
using GymApp.Server.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GymApp.Server.Endpoints;

/// <summary>
/// Shared gym check-ins. The map's privacy rules are enforced here, not just in the app: nothing is stored unless the
/// user shares, turning sharing off deletes everything, a custom gym never carries a position, and friends only see
/// accepted friends who share, from the last 30 days. Distances stay on the phone: the viewer's position is never sent.
/// </summary>
public static class CheckInEndpoints
{
    public static void MapCheckIns(this IEndpointRouteBuilder app)
    {
        app.MapPut("/v1/me/sharing", SetSharing);
        app.MapPut("/v1/me/avatar", SetAvatar);
        app.MapPut("/v1/me/checkins", ReplaceCheckIns);
        app.MapPut("/v1/me/checkins/{date}", PutCheckIn);
        app.MapDelete("/v1/me/checkins/{date}", DeleteCheckIn);
        app.MapGet("/v1/friends/checkins", GetFriendCheckIns);
    }

    private static async Task<IResult> SetAvatar(AvatarRequest request, ClaimsPrincipal me, ServerDbContext db)
    {
        if (request.Avatar is not null && AvatarChoice.Parse(request.Avatar) is null)
            return Problems.BadRequest("That avatar doesn't exist.");
        var userId = me.UserId();
        var updated = await db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Avatar, request.Avatar));
        return updated == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static async Task<IResult> SetSharing(SharingRequest request, ClaimsPrincipal me, ServerDbContext db)
    {
        var userId = me.UserId();
        await using var tx = await db.Database.BeginTransactionAsync();
        var updated = await db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.ShareGymCheckIns, request.ShareGymCheckIns));
        if (updated == 0) return Results.NotFound();
        if (!request.ShareGymCheckIns)
            await db.CheckIns.Where(c => c.UserId == userId).ExecuteDeleteAsync();
        await tx.CommitAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> PutCheckIn(string date, CheckInRequest request, ClaimsPrincipal me, ServerDbContext db,
        TimeProvider clock, CheckInCleanup cleanup)
    {
        if (ClientDates.Parse(date) is not { } day) return Problems.BadRequest($"Send the date as {ApiFormat.Date}.");
        if (day.DayNumber > clock.ServerToday().DayNumber + ClientDates.AllowanceDays)
            return Problems.BadRequest("A check-in can't be in the future.");
        if (Check(request) is { } error) return Problems.BadRequest(error);

        // Before this upload's own write, so an old date it sends isn't swept straight away
        await cleanup.RunIfDueAsync(db);
        var userId = me.UserId();
        var sharing = await db.Users.Where(u => u.Id == userId).Select(u => u.ShareGymCheckIns).FirstOrDefaultAsync();
        if (sharing != true) return Problems.Forbidden("Turn on sharing gym check-ins first.");

        var row = await db.CheckIns.FirstOrDefaultAsync(c => c.UserId == userId && c.Date == day);
        if (row is null)
        {
            row = new CheckIn { UserId = userId, Date = day };
            db.CheckIns.Add(row);
        }
        Fill(row, request);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Problems.Conflict("That check-in just changed. Try again.");
        }
        return Results.NoContent();
    }

    /// <summary>
    /// Replaces all of the user's check-ins in one transaction, for resyncs. Same rules as the single PUT, plus at most
    /// <see cref="StatsEndpoints.MaxDays"/> entries and no date twice. Entries older than the server keeps are dropped,
    /// not refused, so the phone can always send its last 31 days. An empty list clears everything.
    /// </summary>
    private static async Task<IResult> ReplaceCheckIns(CheckInsUploadRequest request, ClaimsPrincipal me, ServerDbContext db,
        TimeProvider clock, CheckInCleanup cleanup)
    {
        var entries = request.CheckIns ?? [];
        if (entries.Count > MaxBulk) return Problems.BadRequest($"Send at most {MaxBulk} check-ins.");
        if (entries.DistinctBy(e => e.Date).Count() != entries.Count) return Problems.BadRequest("Each date can only be sent once.");
        var today = clock.ServerToday();
        if (entries.Any(e => e.Date.DayNumber > today.DayNumber + ClientDates.AllowanceDays))
            return Problems.BadRequest("A check-in can't be in the future.");
        foreach (var e in entries)
            if (Check(Gym(e)) is { } error) return Problems.BadRequest($"{e.Date.ToString(ApiFormat.Date, CultureInfo.InvariantCulture)}: {error}");

        await cleanup.RunIfDueAsync(db);
        var userId = me.UserId();
        await using var tx = await db.Database.BeginTransactionAsync();
        var sharing = await db.Users.Where(u => u.Id == userId).Select(u => u.ShareGymCheckIns).FirstOrDefaultAsync();
        if (sharing != true) return Problems.Forbidden("Turn on sharing gym check-ins first.");

        await db.CheckIns.Where(c => c.UserId == userId).ExecuteDeleteAsync();
        var cutoff = today.AddDays(-CheckInCleanup.KeepDays);
        foreach (var e in entries.Where(e => e.Date >= cutoff))
        {
            var row = new CheckIn { UserId = userId, Date = e.Date };
            Fill(row, Gym(e));
            db.CheckIns.Add(row);
        }

        try
        {
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another upload from this user landed in between
            return Problems.Conflict("That check-in just changed. Try again.");
        }
        return Results.NoContent();
    }

    /// <summary>The log was undone or moved to another day. Nothing to delete is fine too.</summary>
    private static async Task<IResult> DeleteCheckIn(string date, ClaimsPrincipal me, ServerDbContext db)
    {
        if (ClientDates.Parse(date) is not { } day) return Problems.BadRequest($"Send the date as {ApiFormat.Date}.");
        var userId = me.UserId();
        await db.CheckIns.Where(c => c.UserId == userId && c.Date == day).ExecuteDeleteAsync();
        return Results.NoContent();
    }

    /// <summary>Accepted friends who share, from <paramref name="since"/> on but never before the 30-day window
    /// (counted from the server's date, the stricter side for any viewer). Newest first.</summary>
    private static async Task<IResult> GetFriendCheckIns(string? since, ClaimsPrincipal me, ServerDbContext db, TimeProvider clock)
    {
        if (ClientDates.Parse(since) is not { } start) return Problems.BadRequest($"Send since as {ApiFormat.Date}.");
        var windowStart = FriendMapRules.WindowStart(clock.ServerToday());
        if (start < windowStart) start = windowStart;

        var userId = me.UserId();
        var friendIds = db.Friendships
            .Where(f => f.Status == FriendshipStatus.Accepted && (f.UserAId == userId || f.UserBId == userId))
            .Select(f => f.UserAId == userId ? f.UserBId : f.UserAId);

        var rows = await (
            from c in db.CheckIns.AsNoTracking()
            join u in db.Users on c.UserId equals u.Id
            where friendIds.Contains(c.UserId) && u.ShareGymCheckIns == true && u.SuspendedAt == null && c.Date >= start
            orderby c.Date descending, u.UsernameNormalized
            select new { CheckIn = c, u.Username, Featured = u.ShareBadges ? u.FeaturedBadge : null }).ToListAsync();

        return Results.Ok(rows.Select(r => new FriendCheckInResponse(
            r.CheckIn.UserId, r.Username, r.CheckIn.Date,
            r.CheckIn.GymOsmType is { } t ? ToContract(t) : null, r.CheckIn.GymOsmId, r.CheckIn.GymName,
            ToContract(r.CheckIn.CityOsmType), r.CheckIn.CityOsmId, r.CheckIn.CityName,
            // Checked again on the way out: a custom gym never comes with a position
            r.CheckIn.GymOsmId is null ? null : r.CheckIn.Latitude,
            r.CheckIn.GymOsmId is null ? null : r.CheckIn.Longitude,
            r.Featured)).ToList());
    }

    private const int MaxBulk = StatsEndpoints.MaxDays;

    private static CheckInRequest Gym(DatedCheckInRequest e) =>
        new(e.GymOsmType, e.GymOsmId, e.GymName, e.CityOsmType, e.CityOsmId, e.CityName, e.Latitude, e.Longitude);

    /// <summary>Copies a checked request onto a row.</summary>
    private static void Fill(CheckIn row, CheckInRequest request)
    {
        var isCustom = request.GymOsmId is null;
        row.GymOsmType = isCustom ? null : ToCore(request.GymOsmType!.Value);
        row.GymOsmId = request.GymOsmId;
        row.GymName = request.GymName.Trim();
        row.CityOsmType = ToCore(request.CityOsmType);
        row.CityOsmId = request.CityOsmId;
        row.CityName = request.CityName.Trim();
        // A custom gym's position may be someone's home: it's dropped, never stored
        row.Latitude = isCustom ? null : request.Latitude;
        row.Longitude = isCustom ? null : request.Longitude;
    }

    private static string? Check(CheckInRequest r)
    {
        // A number that isn't one of the types reads as an enum too; refused here, not a 500 further down
        if (!Enum.IsDefined(r.CityOsmType) || r.GymOsmType is { } gymType && !Enum.IsDefined(gymType)) return "Unknown OSM type.";
        if (string.IsNullOrWhiteSpace(r.GymName)) return "Send the gym's name.";
        if (r.GymName.Trim().Length > CheckIn.MaxNameLength) return $"The gym's name must be at most {CheckIn.MaxNameLength} characters.";
        if (r.GymOsmType is null != r.GymOsmId is null) return "Send both the gym's OSM type and id, or neither for a custom gym.";
        if (string.IsNullOrWhiteSpace(r.CityName)) return "Send the city's name.";
        if (r.CityName.Trim().Length > CheckIn.MaxNameLength) return $"The city's name must be at most {CheckIn.MaxNameLength} characters.";
        if (r.Latitude is null != r.Longitude is null) return "Send both coordinates, or neither.";
        if (r.Latitude is < -90 or > 90 || r.Longitude is < -180 or > 180) return "Those coordinates aren't on the map.";
        return null;
    }

    private static OsmElementType ToCore(OsmType t) => t switch
    {
        OsmType.Node => OsmElementType.Node,
        OsmType.Way => OsmElementType.Way,
        OsmType.Relation => OsmElementType.Relation,
        _ => throw new ArgumentOutOfRangeException(nameof(t))
    };

    private static OsmType ToContract(OsmElementType t) => t switch
    {
        OsmElementType.Node => OsmType.Node,
        OsmElementType.Way => OsmType.Way,
        OsmElementType.Relation => OsmType.Relation,
        _ => throw new ArgumentOutOfRangeException(nameof(t))
    };
}
