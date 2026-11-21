namespace GymApp.Contracts;

/// <summary>One earned badge (Phase 25). <see cref="Tier"/> is 1 to 4 for a tiered badge, 0 for a one-time one;
/// <see cref="EarnedOn"/> is when the current tier was reached. Ids are the app's stable badge ids, at most 40 chars;
/// a client skips ids it doesn't know.</summary>
public sealed record BadgeDto(string Id, int Tier, DateOnly EarnedOn);

/// <summary>Replaces every badge the server holds for you (<c>PUT /v1/me/badges</c>). At most 200 badges, no id twice.
/// <see cref="Featured"/> is the badge shown next to your name (one of <see cref="Badges"/>, or null for none);
/// <see cref="Share"/> whether friends may see your badges and featured badge.</summary>
public sealed record BadgesUploadRequest(IReadOnlyList<BadgeDto> Badges, string? Featured, bool Share);

/// <summary>A friend's badges (<c>GET /v1/friends/{userId}/badges</c>). Empty, with no featured badge, when they don't
/// share them.</summary>
public sealed record UserBadgesResponse(int UserId, IReadOnlyList<BadgeDto> Badges, string? Featured);
