using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GymApp.Client;
using GymApp.Contracts;
using GymApp.Data;
using GymApp.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

/// <summary>Friend notifications on this phone, for Profile's switch and Friends' card.</summary>
public sealed record PushState(bool IsSupported, bool? Enabled, bool CardDismissed, PushPermission Permission);

public enum PushSwitchResult { On, Off, Refused }

/// <summary>
/// Friend notifications on this phone (Phase 22): Profile's switch, Friends' card, and keeping the server's copy of the
/// push token in step. <see cref="RemotePushRegistration"/> talks to the server; <see cref="LocalPushRegistration"/>
/// (the TestBench's local mode) keeps the switch and sends nothing. Never throws.
/// </summary>
public interface IPushRegistration
{
    Task<PushState> GetStateAsync();

    /// <summary>On asks for the permission first if needed; refused, the switch stays off (<see cref="PushSwitchResult.Refused"/>).
    /// Either way the server follows.</summary>
    Task<PushSwitchResult> SetEnabledAsync(bool on);

    /// <summary>"Not now" on Friends' card.</summary>
    Task DismissCardAsync();

    /// <summary>Brings the server in line in the background: after sign-in, on resume, when the token changes.</summary>
    void RequestSync();

    /// <summary>The same, awaited.</summary>
    Task SyncAsync();

    /// <summary>Waits for the background syncs started so far (tests).</summary>
    Task WhenIdleAsync();
}

/// <summary>The switch and the card, the same in both wirings. The subclass decides what reaching the server means.</summary>
public abstract class PushRegistrationBase(
    IPushNotifications device, IDbContextFactory<AppDbContext> dbFactory, SessionService session, ErrorLog? log) : IPushRegistration
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<Task> _running = [];

    protected IPushNotifications Device => device;

    public async Task<PushState> GetStateAsync()
    {
        if (!device.IsSupported || session.CurrentUser is not { } user) return new(false, null, false, PushPermission.Denied);
        var permission = await device.GetPermissionAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await db.Users.AsNoTracking().Where(u => u.Id == user.Id)
            .Select(u => new { u.FriendNotifications, u.FriendNotificationsCardDismissed }).FirstOrDefaultAsync();
        return new(true, row?.FriendNotifications, row?.FriendNotificationsCardDismissed ?? false, permission);
    }

    public async Task<PushSwitchResult> SetEnabledAsync(bool on)
    {
        if (session.CurrentUser is not { } user || (on && !device.IsSupported)) return PushSwitchResult.Off;
        var result = on ? PushSwitchResult.On : PushSwitchResult.Off;
        if (on && await device.GetPermissionAsync() != PushPermission.Allowed && !await device.RequestPermissionAsync())
            result = PushSwitchResult.Refused;
        await SaveAsync(user.Id, u => u.FriendNotifications = result == PushSwitchResult.On);
        await SyncAsync();
        return result;
    }

    public Task DismissCardAsync() =>
        session.CurrentUser is { } user ? SaveAsync(user.Id, u => u.FriendNotificationsCardDismissed = true) : Task.CompletedTask;

    public void RequestSync()
    {
        var task = Task.Run(SyncAsync);
        lock (_running)
        {
            _running.RemoveAll(t => t.IsCompleted);
            _running.Add(task);
        }
    }

    public async Task WhenIdleAsync()
    {
        while (true)
        {
            Task[] running;
            lock (_running) running = _running.Where(t => !t.IsCompleted).ToArray();
            if (running.Length == 0) return;
            await Task.WhenAll(running);
        }
    }

    public async Task SyncAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (session.CurrentUser is not { } user || !device.IsSupported) return;
            var permission = await device.GetPermissionAsync();
            bool? enabled;
            await using (var db = await dbFactory.CreateDbContextAsync())
                enabled = await db.Users.Where(u => u.Id == user.Id).Select(u => u.FriendNotifications).FirstOrDefaultAsync();
            // The phone already lets the app notify (Reminders asked): on, without a card or a question
            if (enabled is null && permission == PushPermission.Allowed)
            {
                await SaveAsync(user.Id, u => u.FriendNotifications = true);
                enabled = true;
            }
            await SyncCoreAsync(user, enabled == true && permission == PushPermission.Allowed);
        }
        catch (Exception e)
        {
            log?.Write(e, "Push");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Registers this phone's token when <paramref name="wanted"/>, removes it otherwise.</summary>
    protected abstract Task SyncCoreAsync(User user, bool wanted);

    private async Task SaveAsync(int userId, Action<User> change)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.Users.FirstOrDefaultAsync(u => u.Id == userId) is not { } row) return;
        change(row);
        await db.SaveChangesAsync();
        if (session.CurrentUser is { } current && current.Id == userId) change(current);
    }
}

/// <summary>The TestBench's local mode: no server to push from, so the switch is all there is.</summary>
public sealed class LocalPushRegistration(
    IPushNotifications device, IDbContextFactory<AppDbContext> dbFactory, SessionService session, ErrorLog? log = null)
    : PushRegistrationBase(device, dbFactory, session, log)
{
    protected override Task SyncCoreAsync(User user, bool wanted) => Task.CompletedTask;
}

/// <summary>What was last registered: the token and the session it was sent for (a hash of the session token, so a new
/// sign-in, which has no device on the server yet, registers again). A null token: removed.</summary>
public sealed record PushRegistrationRecord(string? Token, PushPlatform Platform, bool Sandbox, string SessionKey);

/// <summary>
/// Keeps the server's copy of this phone's push token in step: registers it when friend notifications are on and allowed,
/// again when the token or the session changes and once a day (in case the server lost it), and removes it when they're
/// turned off. Offline, or a server before 0.22.0 (404), leaves it for the next resume. Logout and account deletion need
/// no call: the server deletes the session's token with the session.
/// </summary>
public sealed class RemotePushRegistration(
    SocialApiClient api, ITokenStore tokens, IPushNotifications device, IDbContextFactory<AppDbContext> dbFactory,
    SessionService session, ErrorLog? log = null, TimeProvider? clock = null)
    : PushRegistrationBase(device, dbFactory, session, log)
{
    /// <summary>An unchanged token is sent again after this, in case the server lost it.</summary>
    public static readonly TimeSpan ResendAfter = TimeSpan.FromDays(1);

    private readonly SocialCacheStore _cache = new(dbFactory, clock);

    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    protected override async Task SyncCoreAsync(User user, bool wanted)
    {
        if (user.ServerUserId is null || tokens.Get() is not { } sessionToken) return;
        var sessionKey = SessionKey(sessionToken);
        var last = await _cache.GetAsync<PushRegistrationRecord>(user.Id, SocialCacheKeys.PushDevice);

        if (!wanted)
        {
            // Only this session's registration is ours to remove; an older session's went with it on the server
            if (last is not ({ Token: not null, SessionKey: var key }, _) || key != sessionKey) return;
            var removed = await api.UnregisterPushDeviceAsync();
            if (!removed.IsOffline) await _cache.SaveAsync(user.Id, SocialCacheKeys.PushDevice, last.Value.Value with { Token = null });
            return;
        }

        if (await Device.GetTokenAsync() is not { } token) return;
        var record = new PushRegistrationRecord(token.Token, token.Platform, token.Sandbox, sessionKey);
        if (last is ({ } previous, var at) && previous == record && Now - at < ResendAfter) return;

        var result = await api.RegisterPushDeviceAsync(new PushDeviceRequest(token.Platform, token.Token, token.Sandbox));
        if (result.IsOk) await _cache.SaveAsync(user.Id, SocialCacheKeys.PushDevice, record);
    }

    /// <summary>A short hash of the session token: tells sessions apart without keeping the token twice.</summary>
    public static string SessionKey(string sessionToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sessionToken)))[..16];
}
