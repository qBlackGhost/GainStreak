using System.Net;
using System.Text;

namespace GymApp.Tests;

/// <summary>Answers HTTP requests from a function instead of the network, and remembers what was asked.</summary>
public sealed class FakeHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = new();
    public List<DateTime> SentAt { get; } = new();

    public static FakeHttpHandler Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        }));

    public static FakeHttpHandler Throws(Exception ex) => new((_, _) => throw ex);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        lock (Requests) { Requests.Add(request); SentAt.Add(DateTime.UtcNow); }
        return respond(request, ct);
    }
}
