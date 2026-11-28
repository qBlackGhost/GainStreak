using System;
using System.Threading.Tasks;
using GymApp.Client;
using GymApp.Contracts;

namespace GymApp.Services;

/// <summary>Reports a user to the developer. Null on success, else the message.</summary>
public interface IUserReportService
{
    Task<string?> ReportAsync(int otherId, UserReportReason reason, string? note);

    /// <summary>When the user can report again (UTC, the 10-minute wait of 0.28.1), or null when they can now.</summary>
    DateTime? WaitUntil => null;
}

/// <summary>Through the social server. Not queued: offline, it says so.</summary>
public class RemoteUserReportService(SocialApiClient api, SendCooldowns? cooldowns = null) : IUserReportService
{
    public DateTime? WaitUntil => cooldowns?.Until(CooldownKind.Report);

    public async Task<string?> ReportAsync(int otherId, UserReportReason reason, string? note)
    {
        var r = await api.ReportUserAsync(otherId, new UserReportRequest(reason, note));
        if (r.IsOk) cooldowns?.Sent(CooldownKind.Report);
        else if (r.RetryAt is { } retryAt) cooldowns?.Refused(CooldownKind.Report, retryAt);
        return r.IsOk ? null : r.Error;
    }
}

/// <summary>The TestBench's local mode has no server to send a report to.</summary>
public class LocalUserReportService : IUserReportService
{
    public const string NoServerMessage = "Reports need the social server.";

    public Task<string?> ReportAsync(int otherId, UserReportReason reason, string? note) => Task.FromResult<string?>(NoServerMessage);
}
