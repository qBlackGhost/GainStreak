using System;
using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

/// <summary>An open cardio timer as Home's card shows it. <see cref="GymName"/> is the gym it's checked against; null
/// when the workout wasn't verified and the timer runs unchecked.</summary>
public record CardioInfo(int Id, DateOnly Date, int? GymId, string? GymName, CardioClock Clock, CardioPause? Pause)
{
    public bool IsChecked => Clock.IsChecked;
    public bool IsRunning => Clock.RunningSince is not null;
    public int Seconds(DateTime now) => CardioRules.Seconds(Clock, now);
}

/// <summary>How a cardio timer ended: the minutes saved onto the log, the points they earn, and the log's points sync
/// for Home. <see cref="Minutes"/> 0: nothing was saved.</summary>
public record CardioEndResult(bool Success, string? Error = null, int Minutes = 0, int Points = 0, LogResult? Log = null);

/// <summary>
/// The cardio timer after a live workout (0.18.2, phones). Started from the prompt after Finish; the minutes go onto
/// the day's workout log through <see cref="WorkoutService.SetCardioForTodayAsync"/>, so they earn points as typed
/// minutes did. When the workout was verified at a gym, the card checks the phone's position while it runs
/// (<see cref="CheckAsync"/>, rules in <see cref="CardioRules"/>); the position itself is never stored. Like a live
/// workout it stops by itself after 4 hours or at midnight, and what it counted is saved.
/// </summary>
/// <param name="clock">For tests; the app uses <see cref="AppClock"/>, so the TestBench can move it.</param>
public class CardioService(IDbContextFactory<AppDbContext> dbFactory, WorkoutService workouts, TimeProvider? clock = null)
{
    private DateTime Now => clock is null ? AppClock.Now : clock.GetLocalNow().DateTime;
    private DateOnly Today => DateOnly.FromDateTime(Now);

    /// <summary>Starts the timer for today's logged workout; checked against its gym when it was verified at one that
    /// has a position. An open timer is returned as it is.</summary>
    public async Task<(CardioInfo? Cardio, string? Error)> StartAsync(int userId)
    {
        if (await GetOpenAsync(userId) is { } open) return (open, null);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var log = await db.WorkoutLogs.AsNoTracking().FirstOrDefaultAsync(w => w.UserId == userId && w.Date == Today);
            if (log is null) return (null, "Log the workout first.");
            int? gymId = null;
            if (log is { Verified: true, GymId: { } id }
                && await db.Gyms.AnyAsync(g => g.Id == id && g.Latitude != null && g.Longitude != null))
                gymId = id;
            var now = Now;
            db.CardioSessions.Add(new CardioSession
            {
                UserId = userId, Date = Today, GymId = gymId, StartedAt = now, RunningSince = now,
                LastAtGymAt = gymId is null ? null : now,   // the workout was just verified there
            });
            await db.SaveChangesAsync();
        }
        return (await GetOpenAsync(userId), null);
    }

    /// <summary>The open timer, or null. One past 4 hours or midnight is stopped and saved here first.</summary>
    public async Task<CardioInfo?> GetOpenAsync(int userId)
    {
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var open = await db.CardioSessions.Include(c => c.Gym).FirstOrDefaultAsync(c => c.UserId == userId && c.EndedAt == null);
            if (open is null) return null;
            if (!LiveWorkoutRules.MustStop(open.StartedAt, open.Date, Now)) return Info(open);
        }
        await StopIfDueAsync(userId);
        return null;
    }

    /// <summary>Saves and ends a timer past 4 hours or midnight. True when it did.</summary>
    public async Task<bool> StopIfDueAsync(int userId)
    {
        int minutes;
        DateOnly date;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var open = await db.CardioSessions.FirstOrDefaultAsync(c => c.UserId == userId && c.EndedAt == null);
            if (open is null || !LiveWorkoutRules.MustStop(open.StartedAt, open.Date, Now)) return false;
            var stopAt = LiveWorkoutRules.StopAt(open.StartedAt, open.Date);
            minutes = CardioRules.Minutes(CardioRules.SecondsAtStop(ClockOf(open), stopAt));
            open.CountedSeconds = CardioRules.SecondsAtStop(ClockOf(open), stopAt);
            open.RunningSince = null;
            open.EndedAt = stopAt;
            open.SavedMinutes = minutes;
            date = open.Date;
            await db.SaveChangesAsync();
        }
        if (minutes > 0) await workouts.SetCardioForTodayAsync(userId, minutes, date);
        return true;
    }

    /// <summary>The user's Pause. The time so far is kept.</summary>
    public Task<CardioInfo?> PauseAsync(int userId) => UpdateAsync(userId, c =>
    {
        if (c.RunningSince is null) return;
        c.CountedSeconds = CardioRules.Seconds(ClockOf(c), Now);
        c.RunningSince = null;
        c.PauseReason = CardioSession.ByUser;
    });

    /// <summary>Resume, after the user's Pause or after leaving the gym. Away from it, the next check pauses it again.</summary>
    public Task<CardioInfo?> ResumeAsync(int userId) => UpdateAsync(userId, c =>
    {
        if (c.RunningSince is not null) return;
        c.RunningSince = Now;
        c.PauseReason = null;
    });

    /// <summary>
    /// One fix from the phone, while the timer is open. Unchecked timers and unusable fixes change nothing (a vague fix
    /// indoors isn't a sign of being elsewhere). At the gym: the time since the last check counts, and a timer paused
    /// for leaving starts again. Away: a running timer pauses, counting only up to the last check that found it there.
    /// </summary>
    public Task<CardioInfo?> CheckAsync(int userId, LocationFix fix) => UpdateAsync(userId, c =>
    {
        if (c.Gym is not { Latitude: { } lat, Longitude: { } lon } gym || !GymProximity.IsUsable(fix)) return;
        var spot = new GymSpot(gym.Id, gym.Name, lat, lon, gym.OwnerUserId is not null);
        if (GymProximity.Nearest(fix, [spot]) is not null)
        {
            c.LastAtGymAt = Now;
            if (c.PauseReason == CardioSession.LeftGym) { c.RunningSince = Now; c.PauseReason = null; }
            return;
        }
        if (c.RunningSince is null) return;
        c.CountedSeconds = CardioRules.SecondsWhenLeft(ClockOf(c));
        c.RunningSince = null;
        c.PauseReason = CardioSession.LeftGym;
    });

    /// <summary>Finish cardio: saves the whole minutes onto the day's log and ends the timer. Under a minute saves
    /// nothing.</summary>
    public async Task<CardioEndResult> FinishAsync(int userId)
    {
        if (await GetOpenAsync(userId) is not { } open) return new CardioEndResult(false, "This cardio session has ended.");
        var minutes = CardioRules.Minutes(open.Seconds(Now));
        LogResult? log = null;
        if (minutes > 0)
        {
            log = await workouts.SetCardioForTodayAsync(userId, minutes, open.Date);
            if (!log.Success) return new CardioEndResult(false, log.Error);
        }
        await UpdateAsync(userId, c =>
        {
            c.CountedSeconds = CardioRules.Seconds(ClockOf(c), Now);
            c.RunningSince = null;
            c.EndedAt = Now;
            c.SavedMinutes = minutes;
        });
        return new CardioEndResult(true, null, minutes, PointsCalculator.CardioPoints(minutes), log);
    }

    private async Task<CardioInfo?> UpdateAsync(int userId, Action<CardioSession> change)
    {
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var open = await db.CardioSessions.Include(c => c.Gym).FirstOrDefaultAsync(c => c.UserId == userId && c.EndedAt == null);
            if (open is null) return null;
            if (!LiveWorkoutRules.MustStop(open.StartedAt, open.Date, Now))
            {
                change(open);
                await db.SaveChangesAsync();
                if (open.EndedAt is not null) return null;
                return Info(open);
            }
        }
        await StopIfDueAsync(userId);
        return null;
    }

    private static CardioClock ClockOf(CardioSession c) => new(c.CountedSeconds, c.RunningSince, c.LastAtGymAt, c.GymId is not null);

    private static CardioInfo Info(CardioSession c) => new(c.Id, c.Date, c.GymId, c.Gym?.Name, ClockOf(c),
        c.PauseReason switch { CardioSession.ByUser => CardioPause.User, CardioSession.LeftGym => CardioPause.LeftGym, _ => null });
}
