using System;

namespace GymApp.Data.Models;

/// <summary>The last successful answer from the social server for one read (see <see cref="SocialCacheKeys"/>), kept per
/// user so friends, requests, the leaderboards and friends' check-ins still show offline, marked "Last updated".</summary>
public class SocialCache
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string Key { get; set; } = "";
    /// <summary>The server's response, in the API's JSON format.</summary>
    public string Json { get; set; } = "";
    public DateTime FetchedAt { get; set; }
}

public static class SocialCacheKeys
{
    public const string Friends = "friends";
    public const string Requests = "requests";
    public const string LeaderboardThisWeek = "leaderboard:thisWeek";
    public const string LeaderboardAllTime = "leaderboard:allTime";
    public const string CheckIns = "checkins";
    public const string SplitUsage = "splitUsage";
}
