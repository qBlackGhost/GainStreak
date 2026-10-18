using System;

namespace GymApp.Core;

/// <summary>The app's "now". Always the real clock in the app; the TestBench shifts <see cref="Offset"/> to try other days.</summary>
public static class AppClock
{
    public static TimeSpan Offset { get; set; }
    public static DateTime Now => DateTime.Now + Offset;
    public static DateOnly Today => DateOnly.FromDateTime(Now);
}
