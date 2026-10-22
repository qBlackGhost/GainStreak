namespace GymApp.Contracts;

/// <summary>One exercise of a shared day. Built-in exercises are matched by <see cref="Name"/> on the receiving phone
/// (ids are local); a custom one (<see cref="IsCustom"/>) is matched by name among the receiver's own, else created.
/// <see cref="MuscleGroup"/> is the app's group name, e.g. "Quads".</summary>
public sealed record SharedExercise(string Name, string MuscleGroup, bool IsCustom, int WarmupSets, int WorkingSets);

/// <summary>One day of a shared split. <see cref="Weekday"/> is set for a custom split (its fixed weekday) and null
/// for a preset, whose days rotate.</summary>
public sealed record SharedDay(string Name, DayOfWeek? Weekday, IReadOnlyList<SharedExercise> Exercises);

/// <summary>A whole split with every day's exercises. <see cref="PresetId"/> is the built-in preset (the same on every
/// phone) with the sender's weekly layout in <see cref="Pattern"/> (7 chars of W / R from day 1); both null for a
/// custom split, whose days carry their weekdays.</summary>
public sealed record SharedSplit(string Name, int? PresetId, string? Pattern, IReadOnlyList<SharedDay> Days);

/// <summary>One workout (a split day's exercise list) and where it came from, for the receiver's card.</summary>
public sealed record SharedWorkout(string DayName, string SplitName, IReadOnlyList<SharedExercise> Exercises);

/// <summary>Exactly one of <see cref="Split"/> and <see cref="Workout"/>, sent to each friend in <see cref="ToUserIds"/>
/// (server ids).</summary>
public sealed record ShareRequest(IReadOnlyList<int> ToUserIds, SharedSplit? Split, SharedWorkout? Workout);

public sealed record SendShareResponse(int Sent);

/// <summary>A share waiting for you, newest first. Removing it (accepted or dismissed) is the receiver's call.</summary>
public sealed record ShareResponse(long Id, int FromUserId, string FromUsername, DateTime SentAt,
    SharedSplit? Split, SharedWorkout? Workout);

/// <summary>Limits shared by the app (checks before sending) and the server (checks again on arrival).</summary>
public static class ShareRules
{
    public const int MaxRecipients = 10;
    /// <summary>Shares waiting for one person; the oldest go first when a new one arrives.</summary>
    public const int MaxPending = 30;
    public static readonly TimeSpan KeptFor = TimeSpan.FromDays(30);
    public const int MaxDays = 7;
    public const int MaxExercises = 20;
    public const int MaxSets = 15;
    public const int MaxSplitName = 40;
    public const int MaxDayName = 20;
    public const int MaxExerciseName = 60;

    public const string NoRecipientsMessage = "Pick at least one friend.";
    public const string TooManyRecipientsMessage = "Share with at most 10 friends at a time.";
    public const string NotFriendsMessage = "You can only share with your friends.";
    public const string OneThingMessage = "Share either a split or a workout.";
    public const string EmptyMessage = "There's nothing to share yet. Add exercises first.";
    public const string InvalidMessage = "That share isn't valid.";

    /// <summary>The first problem with a request, or null when it can be sent.</summary>
    public static string? Validate(ShareRequest request)
    {
        var ids = request.ToUserIds ?? [];
        if (ids.Count == 0) return NoRecipientsMessage;
        if (ids.Distinct().Count() > MaxRecipients) return TooManyRecipientsMessage;
        if ((request.Split is null) == (request.Workout is null)) return OneThingMessage;

        if (request.Workout is { } w)
        {
            if (!Name(w.DayName, MaxDayName) || !Name(w.SplitName, MaxSplitName)) return InvalidMessage;
            if (w.Exercises is not { Count: > 0 }) return EmptyMessage;
            return Exercises(w.Exercises) ? null : InvalidMessage;
        }

        var s = request.Split!;
        if (!Name(s.Name, MaxSplitName) || s.Days is not { Count: > 0 and <= MaxDays }) return InvalidMessage;
        if ((s.PresetId is null) != (s.Pattern is null)) return InvalidMessage;
        if (s.Pattern is { } p && (p.Length != 7 || p.Any(c => c is not ('W' or 'R')) || !p.Contains('W'))) return InvalidMessage;
        if (s.PresetId is null && (s.Days.Any(d => d.Weekday is null) || s.Days.Select(d => d.Weekday).Distinct().Count() != s.Days.Count))
            return InvalidMessage;
        if (s.Days.Any(d => !Name(d.Name, MaxDayName) || d.Exercises is null || d.Exercises.Count > MaxExercises || !Exercises(d.Exercises)))
            return InvalidMessage;
        return s.Days.All(d => d.Exercises.Count == 0) ? EmptyMessage : null;
    }

    private static bool Name(string? name, int max) => !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= max;

    private static bool Exercises(IReadOnlyList<SharedExercise> list) =>
        list.Count <= MaxExercises && list.All(e =>
            Name(e.Name, MaxExerciseName) && Name(e.MuscleGroup, 20)
            && e.WarmupSets is >= 0 and <= MaxSets && e.WorkingSets is >= 0 and <= MaxSets);
}
