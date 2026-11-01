using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GymApp.Contracts;
using GymApp.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Server.Feedback;

/// <summary>
/// Posts each new feedback and user report to a Discord channel through its webhook (<c>Feedback:DiscordWebhookUrl</c>, env
/// <c>Feedback__DiscordWebhookUrl</c>). Unset, nothing is forwarded and feedback is only stored. The note goes in an
/// embed; the error report (and a note too long for the embed) as an attached text file. Mentions are switched off, so
/// "@everyone" in a note is just text. A failed post is logged and the row keeps a null <see cref="FeedbackEntry.ForwardedAt"/>.
/// Posting happens inside the feedback request, not in a background queue: a host that scales to zero (Cloud Run) only
/// runs the app while a request is open, so work left for after the response may never happen. Each new feedback also
/// retries the recent rows that didn't go through.
/// </summary>
public sealed class DiscordFeedbackForwarder(
    IServiceScopeFactory scopes, IHttpClientFactory http, Uri webhook, TimeProvider clock,
    ILogger<DiscordFeedbackForwarder> log)
{
    public const string SettingName = "Feedback:DiscordWebhookUrl";
    public const string HttpClientName = "discord";

    /// <summary>Discord's limit on an embed description is 4096; past this the note goes in the file instead.</summary>
    public const int MaxEmbedText = 4_000;

    /// <summary>Unforwarded rows older than this are left alone: Discord refusing one for a week means it never will.</summary>
    public static readonly TimeSpan RetryWindow = TimeSpan.FromDays(7);

    /// <summary>The most rows one pass posts, so a long Discord outage doesn't make one request slow.</summary>
    public const int MaxPerPass = 10;

    /// <summary>The configured webhook, null when unset. Throws for a value that isn't an https URL, so a typo stops
    /// startup instead of silently dropping every report.</summary>
    public static Uri? ReadWebhook(IConfiguration config)
    {
        var text = config[SettingName];
        if (string.IsNullOrWhiteSpace(text)) return null;
        return Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? uri
            : throw new InvalidOperationException($"{SettingName.Replace(":", "__")} must be a Discord webhook URL starting with https://.");
    }

    /// <summary>Posts the unforwarded feedback from the last <see cref="RetryWindow"/>, oldest first, at most
    /// <see cref="MaxPerPass"/>. Stops at the first failure, since Discord is likely down for the rest too. Never
    /// throws; returns how many went through.</summary>
    public Task<int> ForwardPendingAsync(CancellationToken ct = default) =>
        ForwardPendingAsync((db, since) => db.Feedback
            .Where(f => f.ForwardedAt == null && f.CreatedAt >= since)
            .OrderBy(f => f.Id).Select(f => f.Id).Take(MaxPerPass).ToListAsync(ct), ForwardAsync, ct);

    /// <summary>The same for user reports.</summary>
    public Task<int> ForwardPendingReportsAsync(CancellationToken ct = default) =>
        ForwardPendingAsync((db, since) => db.UserReports
            .Where(r => r.ForwardedAt == null && r.CreatedAt >= since)
            .OrderBy(r => r.Id).Select(r => r.Id).Take(MaxPerPass).ToListAsync(ct), ForwardReportAsync, ct);

    private async Task<int> ForwardPendingAsync(Func<ServerDbContext, DateTime, Task<List<int>>> pending,
        Func<int, CancellationToken, Task<bool>> forward, CancellationToken ct)
    {
        var forwarded = 0;
        try
        {
            List<int> ids;
            await using (var scope = scopes.CreateAsyncScope())
                ids = await pending(scope.ServiceProvider.GetRequiredService<ServerDbContext>(), clock.GetUtcNow().UtcDateTime - RetryWindow);
            foreach (var id in ids)
            {
                if (!await forward(id, ct)) break;
                forwarded++;
            }
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log.LogError(e, "Forwarding feedback to Discord failed.");
        }
        return forwarded;
    }

    /// <summary>Posts one stored feedback and marks it forwarded. False if Discord refused it.</summary>
    public async Task<bool> ForwardAsync(int feedbackId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
        var row = await (from f in db.Feedback
                         join u in db.Users on f.UserId equals u.Id
                         where f.Id == feedbackId
                         select new { Entry = f, u.Username }).FirstOrDefaultAsync(ct);
        if (row is null) return false;   // deleted with its account meanwhile

        using var content = BuildMessage(row.Entry, row.Username);
        using var response = await http.CreateClient(HttpClientName).PostAsync(webhook, content, ct);
        if (!response.IsSuccessStatusCode)
        {
            log.LogWarning("Discord refused feedback {Id}: {Status}.", feedbackId, (int)response.StatusCode);
            return false;
        }
        var now = clock.GetUtcNow().UtcDateTime;
        await db.Feedback.Where(f => f.Id == feedbackId).ExecuteUpdateAsync(s => s.SetProperty(f => f.ForwardedAt, now), ct);
        return true;
    }

    /// <summary>Posts one stored user report and marks it forwarded. False if Discord refused it.</summary>
    public async Task<bool> ForwardReportAsync(int reportId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
        var row = await (from r in db.UserReports
                         join sender in db.Users on r.ReporterId equals sender.Id
                         join target in db.Users on r.ReportedUserId equals target.Id
                         where r.Id == reportId
                         select new { Report = r, Reporter = sender.Username, Reported = target.Username }).FirstOrDefaultAsync(ct);
        if (row is null) return false;   // deleted with an account meanwhile

        using var content = BuildReportMessage(row.Report, row.Reporter, row.Reported);
        using var response = await http.CreateClient(HttpClientName).PostAsync(webhook, content, ct);
        if (!response.IsSuccessStatusCode)
        {
            log.LogWarning("Discord refused user report {Id}: {Status}.", reportId, (int)response.StatusCode);
            return false;
        }
        var now = clock.GetUtcNow().UtcDateTime;
        await db.UserReports.Where(r => r.Id == reportId).ExecuteUpdateAsync(s => s.SetProperty(r => r.ForwardedAt, now), ct);
        return true;
    }

    /// <summary>The webhook body for a user report: one embed, with the ids to act on. A note is at most
    /// <see cref="UserReportRules.MaxNoteLength"/> characters, so it always fits.</summary>
    public static MultipartFormDataContent BuildReportMessage(UserReport report, string reporter, string reported)
    {
        var payload = new
        {
            username = "GainStreak reports",
            allowed_mentions = new { parse = Array.Empty<string>() },
            embeds = new[]
            {
                new
                {
                    title = "User report: " + UserReportRules.Label(report.Reason),
                    description = report.Note ?? "(No note.)",
                    color = 0xFFB020,
                    fields = new[]
                    {
                        new { name = "Reported", value = $"{reported} (user {report.ReportedUserId.ToString(CultureInfo.InvariantCulture)})", inline = true },
                        new { name = "From", value = $"{reporter} (user {report.ReporterId.ToString(CultureInfo.InvariantCulture)})", inline = true },
                        new { name = "Id", value = "#" + report.Id.ToString(CultureInfo.InvariantCulture), inline = true }
                    },
                    timestamp = DateTime.SpecifyKind(report.CreatedAt, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture)
                }
            }
        };

        var json = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8);
        json.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return new MultipartFormDataContent { { json, "payload_json" } };
    }

    /// <summary>The webhook body: <c>payload_json</c> with one embed, plus <c>files[0]</c> when there's a report or
    /// the note is too long for the embed.</summary>
    public static MultipartFormDataContent BuildMessage(FeedbackEntry entry, string username)
    {
        var isProblem = entry.Kind == FeedbackKind.Problem;
        var cut = entry.Text.Length > MaxEmbedText;
        var description = cut ? entry.Text[..MaxEmbedText] + "…\n\n(The full note is in the attached file.)" : entry.Text;

        var payload = new
        {
            username = "GainStreak feedback",
            allowed_mentions = new { parse = Array.Empty<string>() },
            embeds = new[]
            {
                new
                {
                    title = isProblem ? "Problem report" : "Idea",
                    description,
                    color = isProblem ? 0xFF6B5B : 0xC6F432,
                    fields = new[]
                    {
                        new { name = "From", value = username, inline = true },
                        new { name = "App", value = entry.AppVersion ?? "unknown", inline = true },
                        new { name = "Id", value = "#" + entry.Id.ToString(CultureInfo.InvariantCulture), inline = true }
                    },
                    timestamp = DateTime.SpecifyKind(entry.CreatedAt, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture)
                }
            }
        };

        var content = new MultipartFormDataContent();
        var json = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8);
        json.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Add(json, "payload_json");

        if (entry.Report is not null || cut)
        {
            var file = new StringBuilder();
            if (cut) file.AppendLine(entry.Text).AppendLine().AppendLine("----");
            if (entry.Report is not null) file.Append(entry.Report);
            var part = new ByteArrayContent(Encoding.UTF8.GetBytes(file.ToString()));
            part.Headers.ContentType = new MediaTypeHeaderValue("text/plain") { CharSet = "utf-8" };
            content.Add(part, "files[0]", $"feedback-{entry.Id}.txt");
        }
        return content;
    }
}
