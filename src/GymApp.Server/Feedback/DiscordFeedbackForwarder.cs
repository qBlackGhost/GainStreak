using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using GymApp.Contracts;
using GymApp.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Server.Feedback;

/// <summary>New feedback ids waiting to go to Discord. Saving never waits on Discord: the endpoint stores the row,
/// queues its id and answers. A full queue drops the id (the row is still stored).</summary>
public sealed class FeedbackQueue
{
    private readonly Channel<int> _ids = Channel.CreateBounded<int>(new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Enqueue(int feedbackId) => _ids.Writer.TryWrite(feedbackId);
    public ChannelReader<int> Reader => _ids.Reader;
}

/// <summary>
/// Posts each new feedback to a Discord channel through its webhook (<c>Feedback:DiscordWebhookUrl</c>, env
/// <c>Feedback__DiscordWebhookUrl</c>). Unset, nothing is forwarded and feedback is only stored. The note goes in an
/// embed; the error report (and a note too long for the embed) as an attached text file. Mentions are switched off, so
/// "@everyone" in a note is just text. A failed post is logged and the row keeps a null <see cref="FeedbackEntry.ForwardedAt"/>.
/// </summary>
public sealed class DiscordFeedbackForwarder(
    FeedbackQueue queue, IServiceScopeFactory scopes, IHttpClientFactory http, Uri webhook, TimeProvider clock,
    ILogger<DiscordFeedbackForwarder> log) : BackgroundService
{
    public const string SettingName = "Feedback:DiscordWebhookUrl";
    public const string HttpClientName = "discord";

    /// <summary>Discord's limit on an embed description is 4096; past this the note goes in the file instead.</summary>
    public const int MaxEmbedText = 4_000;

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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var id in queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await ForwardAsync(id, stoppingToken);
                }
                catch (Exception e) when (e is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    log.LogError(e, "Forwarding feedback {Id} to Discord failed.", id);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down
        }
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
