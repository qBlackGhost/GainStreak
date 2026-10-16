using System;
using System.Linq;
using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Data;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

/// <summary>The monthly "gyms visited" summary. Recomputed from the workout logs every time, never stored.</summary>
public class GymSummaryService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<MonthSummary> GetMonthAsync(int userId, int year, int month)
    {
        var (first, last) = GymSummaries.MonthRange(year, month);
        await using var db = await dbFactory.CreateDbContextAsync();

        // Gyms are loaded whatever their state: closed or deleted gyms still count in history
        var rows = await db.WorkoutLogs.AsNoTracking()
            .Where(w => w.UserId == userId && w.Date >= first && w.Date <= last)
            .Select(w => new
            {
                w.Date,
                Gym = w.Gym == null ? null : new
                {
                    w.Gym.Id, w.Gym.OsmType, w.Gym.OsmId, w.Gym.Name, w.Gym.IsActive, w.Gym.OwnerUserId,
                    Address = w.Gym.Address ?? w.Gym.LookedUpAddress
                }
            })
            .ToListAsync();

        var logs = rows.Select(r => new MonthLog(r.Date, r.Gym is not { } g ? null : new VisitedGym(
            g.OsmType is { } t && g.OsmId is { } id ? VisitedGym.OsmKey(t, id) : VisitedGym.CustomKey(g.Id),
            g.Name, g.Address, g.IsActive, g.OwnerUserId is not null)));

        return GymSummaries.Build(year, month, logs);
    }
}
