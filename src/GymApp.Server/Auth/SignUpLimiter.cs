namespace GymApp.Server.Auth;

/// <summary>
/// Counts the accounts actually created, per address (<see cref="RateLimits.AddressKey"/>) and across the server, over
/// the last hour. Only a successful sign-up counts: a taken username or a weak password doesn't use up a try, so people
/// signing up behind one mobile network's shared address (carrier-grade NAT) don't lock each other out. Failed attempts
/// are still capped by the looser <see cref="RateLimits.Register"/> policy. Across the server nothing is refused: past
/// <see cref="AlertPerHour"/> sign-ups in an hour, the caller posts one alert (a hard cap would let a script lock
/// everyone out). In memory, so a restart starts the counts over; Cloud Run runs one instance.
/// </summary>
public sealed class SignUpLimiter(TimeProvider clock)
{
    /// <summary>Accounts one address may create in an hour.</summary>
    public const int PerAddressPerHour = 30;

    /// <summary>Sign-ups in an hour across the server that trigger an alert; at most one alert an hour.</summary>
    public const int AlertPerHour = 100;

    public static readonly TimeSpan Window = TimeSpan.FromHours(1);

    /// <summary>Past this many tracked addresses, a sign-up also drops the ones with nothing left in the window.</summary>
    private const int SweepAbove = 1_000;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> _byAddress = [];
    private readonly Queue<DateTimeOffset> _all = new();
    private DateTimeOffset? _lastAlert;

    /// <summary>Whether <paramref name="addressKey"/> has used up its sign-ups for the hour.</summary>
    public bool IsFull(string addressKey)
    {
        lock (_lock)
        {
            var now = clock.GetUtcNow();
            return _byAddress.TryGetValue(addressKey, out var times) && Prune(times, now) >= PerAddressPerHour;
        }
    }

    /// <summary>Records a created account. Returns the server-wide count of the last hour when it calls for an alert
    /// (at least <see cref="AlertPerHour"/>, and no alert in the last hour), otherwise null.</summary>
    public int? Record(string addressKey)
    {
        lock (_lock)
        {
            var now = clock.GetUtcNow();
            if (!_byAddress.TryGetValue(addressKey, out var times))
            {
                if (_byAddress.Count >= SweepAbove) Sweep(now);
                _byAddress[addressKey] = times = new Queue<DateTimeOffset>();
            }
            times.Enqueue(now);
            _all.Enqueue(now);

            var total = Prune(_all, now);
            if (total < AlertPerHour || now - _lastAlert < Window) return null;
            _lastAlert = now;
            return total;
        }
    }

    private static int Prune(Queue<DateTimeOffset> times, DateTimeOffset now)
    {
        while (times.Count > 0 && now - times.Peek() >= Window) times.Dequeue();
        return times.Count;
    }

    private void Sweep(DateTimeOffset now)
    {
        foreach (var (key, times) in _byAddress.ToList())
            if (Prune(times, now) == 0) _byAddress.Remove(key);
    }
}
