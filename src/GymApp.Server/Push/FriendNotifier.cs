using System.Collections.Concurrent;
using GymApp.Contracts;
using GymApp.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Server.Push;

/// <summary>How one device's push went, for the logs and the <c>push-test</c> command. Never carries the token.</summary>
public sealed record PushDelivery(long DeviceId, PushPlatform Platform, bool Sandbox, PushSendResult Result);

/// <summary>
/// Sends the friend notifications (Phase 22) to every live device of the receiver, through the configured
/// <see cref="IPushSender"/>s. Called by the friend endpoints after their rule succeeded, inside the request: Cloud Run
/// only gives the container CPU while a request is open, so work left for after the response may never run (as with
/// <see cref="Feedback.DiscordFeedbackForwarder"/>). <see cref="TimeLimit"/> bounds the wait, and nothing here ever
/// throws, so a push never changes the friend endpoint's answer. Tokens a service reports dead are deleted.
/// </summary>
public sealed class FriendNotifier(
    IDbContextFactory<ServerDbContext> dbs, IEnumerable<IPushSender> senders, TimeProvider clock, ILogger<FriendNotifier> log)
{
    public static readonly TimeSpan DefaultTimeLimit = TimeSpan.FromSeconds(3);

    /// <summary>At most one "wants to be your friend" push per sender and receiver in this time, so request → cancel →
    /// request can't be used to buzz someone.</summary>
    public static readonly TimeSpan RequestRepeatAfter = TimeSpan.FromHours(12);

    /// <summary>The most devices one user gets a push on: the most recently used sessions.</summary>
    public const int MaxDevices = 10;

    /// <summary>How long one event's sends may take together, all devices in parallel.</summary>
    public TimeSpan TimeLimit { get; init; } = DefaultTimeLimit;

    private readonly Dictionary<PushPlatform, IPushSender> _senders =
        senders.GroupBy(s => s.Platform).ToDictionary(g => g.Key, g => g.Last());

    /// <summary>When each (sender, receiver) last got a request push. In memory: the server runs one instance, and after
    /// a cold start the worst case is one extra push.</summary>
    private readonly ConcurrentDictionary<(int From, int To), DateTime> _requestPushes = new();

    /// <summary>Whether any push service is configured.</summary>
    public bool IsConfigured => _senders.Count > 0;

    /// <summary>Tells <paramref name="receiverId"/> what <paramref name="senderId"/> did. Never throws.</summary>
    public async Task<IReadOnlyList<PushDelivery>> NotifyAsync(int receiverId, PushKind kind, int senderId)
    {
        try
        {
            if (!IsConfigured) return [];
            if (kind == PushKind.FriendRequest && !TakeRequestSlot(senderId, receiverId)) return [];

            await using var db = await dbs.CreateDbContextAsync();
            var username = await db.Users.Where(u => u.Id == senderId).Select(u => u.Username).FirstOrDefaultAsync();
            if (username is null) return [];
            return await SendAsync(db, receiverId, new PushMessage(kind, senderId, username));
        }
        catch (Exception e)
        {
            log.LogError(e, "Sending a {Kind} push to user {UserId} failed.", kind, receiverId);
            return [];
        }
    }

    /// <summary>Sends <paramref name="message"/> to the user's live devices, with no repeat rule (the <c>push-test</c>
    /// command, 22b). Never throws.</summary>
    public async Task<IReadOnlyList<PushDelivery>> SendToUserAsync(int userId, PushMessage message)
    {
        try
        {
            if (!IsConfigured) return [];
            await using var db = await dbs.CreateDbContextAsync();
            return await SendAsync(db, userId, message);
        }
        catch (Exception e)
        {
            log.LogError(e, "Sending a push to user {UserId} failed.", userId);
            return [];
        }
    }

    private async Task<IReadOnlyList<PushDelivery>> SendAsync(ServerDbContext db, int userId, PushMessage message)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var platforms = _senders.Keys.ToList();
        var devices = await (from d in db.PushDevices
                             join s in db.Sessions on d.SessionId equals s.Id
                             where d.UserId == userId && s.ExpiresAt > now && platforms.Contains(d.Platform)
                             orderby s.LastUsedAt descending
                             select d).AsNoTracking().Take(MaxDevices).ToListAsync();
        if (devices.Count == 0) return [];

        using var stop = new CancellationTokenSource(TimeLimit, clock);
        var sends = devices.Select(d => SendOneAsync(d, message, stop.Token)).ToArray();
        try
        {
            await Task.WhenAll(sends).WaitAsync(TimeLimit, clock);
        }
        catch (TimeoutException)
        {
            log.LogWarning("A {Kind} push to user {UserId} took longer than {Limit} s; answered without waiting.",
                message.Kind, userId, TimeLimit.TotalSeconds);
        }

        var deliveries = devices.Select((d, i) => new PushDelivery(d.Id, d.Platform, d.Sandbox,
            sends[i].IsCompletedSuccessfully ? sends[i].Result : PushSendResult.Failed)).ToList();
        var dead = deliveries.Where(x => x.Result == PushSendResult.DeadToken).Select(x => x.DeviceId).ToList();
        if (dead.Count > 0) await db.PushDevices.Where(d => dead.Contains(d.Id)).ExecuteDeleteAsync();
        return deliveries;
    }

    private async Task<PushSendResult> SendOneAsync(PushDevice device, PushMessage message, CancellationToken ct)
    {
        try
        {
            return await _senders[device.Platform].SendAsync(new PushTarget(device.Platform, device.Token, device.Sandbox), message, ct);
        }
        catch (Exception e)
        {
            if (!ct.IsCancellationRequested) log.LogError(e, "{Platform} push to device {DeviceId} failed.", device.Platform, device.Id);
            return PushSendResult.Failed;
        }
    }

    /// <summary>True, and remembers now, unless this pair had a request push within <see cref="RequestRepeatAfter"/>.</summary>
    private bool TakeRequestSlot(int from, int to)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (_requestPushes.TryGetValue((from, to), out var last) && now - last < RequestRepeatAfter) return false;
        _requestPushes[(from, to)] = now;
        if (_requestPushes.Count > 10_000)
            foreach (var (key, at) in _requestPushes)
                if (now - at >= RequestRepeatAfter) _requestPushes.TryRemove(key, out _);
        return true;
    }
}
