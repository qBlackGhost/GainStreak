using System.Net;
using System.Net.Http.Json;
using GymApp.Client;
using GymApp.Contracts;

namespace GymApp.Tests;

/// <summary>Answers social-server routes ("POST v1/auth/login") from handlers a test sets; anything else is a 404.
/// <see cref="Offline"/> makes every call fail as if there were no connection.</summary>
public sealed class FakeSocialServer
{
    private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new();

    public FakeSocialServer()
    {
        Handler = new FakeHttpHandler((req, _) =>
        {
            if (Offline) throw new HttpRequestException("No route to host");
            var key = $"{req.Method} {req.RequestUri!.AbsolutePath.TrimStart('/')}";
            Calls.Add(key);
            lock (Bodies) Bodies.Add((key, req.Content?.ReadAsStringAsync().GetAwaiter().GetResult()));
            return Task.FromResult(_routes.TryGetValue(key, out var route) ? route(req) : new HttpResponseMessage(HttpStatusCode.NotFound));
        });
        Tokens = new MemoryTokenStore();
        // The badges go up after every stats upload (Phase 25); a test can still set its own answer
        On("PUT v1/me/badges", HttpStatusCode.NoContent);
        Api = new SocialApiClient(new HttpClient(Handler) { BaseAddress = new Uri("http://social.test/") }, Tokens);
    }

    public FakeHttpHandler Handler { get; }
    public MemoryTokenStore Tokens { get; }
    public SocialApiClient Api { get; }
    public bool Offline { get; set; }
    public List<string> Calls { get; } = new();
    public List<(string Route, string? Body)> Bodies { get; } = new();

    /// <summary>The last body sent to <paramref name="route"/>, read as <typeparamref name="T"/>.</summary>
    public T? LastBody<T>(string route)
    {
        lock (Bodies)
            return Bodies.LastOrDefault(b => b.Route == route).Body is { } json
                ? System.Text.Json.JsonSerializer.Deserialize<T>(json, ApiFormat.Json)
                : default;
    }

    public void On(string route, Func<HttpRequestMessage, HttpResponseMessage> respond) => _routes[route] = respond;
    public void On(string route, HttpStatusCode status, object? body = null) => On(route, _ => Respond(status, body));

    public static HttpResponseMessage Respond(HttpStatusCode status, object? body = null) =>
        new(status) { Content = body is null ? null : JsonContent.Create(body, body.GetType(), options: ApiFormat.Json) };

    public static object Problem(int status, string detail, string? field = null) =>
        field is null ? new { status, detail } : new { status, detail, field };

    /// <summary>Login and register both answer with this account and token.</summary>
    public void Accepts(MeResponse me, string token = "token-1")
    {
        On("POST v1/auth/login", HttpStatusCode.OK, new AuthResponse(token, me));
        On("POST v1/auth/register", HttpStatusCode.Created, new AuthResponse(token, me));
        On("GET v1/me", HttpStatusCode.OK, me);
    }

    public void RefusesLogin() =>
        On("POST v1/auth/login", HttpStatusCode.Unauthorized, Problem(401, "Wrong username or password."));
}
