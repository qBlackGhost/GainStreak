using System.Globalization;
using GymApp.Contracts;

namespace GymApp.Server;

/// <summary>Days are local to each phone, so clients send their own date. It's trusted only near the server's UTC date:
/// time zones put a real phone at most a day either side, and 2 leaves room for a clock that's slightly off.</summary>
public static class ClientDates
{
    public const int AllowanceDays = 2;

    public static DateOnly ServerToday(this TimeProvider clock) => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    public static bool IsNearServerDate(this TimeProvider clock, DateOnly date) =>
        Math.Abs(date.DayNumber - clock.ServerToday().DayNumber) <= AllowanceDays;

    /// <summary>Parses a yyyy-MM-dd query value; null if it's missing or in another format.</summary>
    public static DateOnly? Parse(string? value) =>
        DateOnly.TryParseExact(value, ApiFormat.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
