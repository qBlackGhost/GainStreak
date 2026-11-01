using System.Threading.Tasks;
using GymApp.Client;
using GymApp.Contracts;

namespace GymApp.Services;

/// <summary>Reports a user to the developer. Null on success, else the message.</summary>
public interface IUserReportService
{
    Task<string?> ReportAsync(int otherId, UserReportReason reason, string? note);
}

/// <summary>Through the social server. Not queued: offline, it says so.</summary>
public class RemoteUserReportService(SocialApiClient api) : IUserReportService
{
    public async Task<string?> ReportAsync(int otherId, UserReportReason reason, string? note)
    {
        var r = await api.ReportUserAsync(otherId, new UserReportRequest(reason, note));
        return r.IsOk ? null : r.Error;
    }
}

/// <summary>The TestBench's local mode has no server to send a report to.</summary>
public class LocalUserReportService : IUserReportService
{
    public const string NoServerMessage = "Reports need the social server.";

    public Task<string?> ReportAsync(int otherId, UserReportReason reason, string? note) => Task.FromResult<string?>(NoServerMessage);
}
