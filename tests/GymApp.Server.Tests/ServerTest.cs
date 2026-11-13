using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GymApp.Client;
using GymApp.Contracts;
using GymApp.Server.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GymApp.Server.Tests;

/// <summary>
/// Base for endpoint tests: the real server in memory, on a fresh, migrated database that is dropped afterwards.
/// xUnit makes one instance per test, so no test sees another's rows.
/// </summary>
[Collection(ServerCollection.Name)]
public abstract class ServerTest(PostgresFixture postgres) : IAsyncLifetime
{
    /// <summary>The server's clock. Real time unless a test sets <see cref="TestClock.Fixed"/>.</summary>
    protected TestClock Clock { get; } = new();

    private WebApplicationFactory<Program> _factory = null!;

    /// <summary>The server's MinClientVersion setting; unset (every build accepted) unless a test class overrides it.</summary>
    protected virtual string? MinClientVersion => null;

    /// <summary>A client with no sign-in.</summary>
    protected HttpClient Client { get; private set; } = null!;

    /// <summary>A context on this test's database, for seeding and checking rows directly.</summary>
    /// <summary>The server's services, for the ones a test drives directly (the check-in cleanup, say).</summary>
    protected IServiceProvider Services => _factory.Services;

    protected ServerDbContext Db() => _factory.Services.CreateScope().ServiceProvider.GetRequiredService<ServerDbContext>();

    public const string DefaultPassword = "correct horse 1";

    /// <summary>A client with no sign-in that returns redirects instead of following them.</summary>
    protected HttpClient ClientWithoutRedirects() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>A client that sends this token on every request.</summary>
    protected HttpClient ClientWith(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>The app's API client on the in-memory server, with its own token store.</summary>
    protected SocialApiClient Api(ITokenStore? tokens = null, string? appVersion = null) =>
        new(_factory.CreateClient(), tokens ?? new MemoryTokenStore(), appVersion: appVersion);

    /// <summary>A phone with <paramref name="username"/> registered and signed in through the app's AccountService.
    /// Several phones register from one test client, so keep within the sign-up limits (SignUpLimiter.PerAddressPerHour per test).</summary>
    protected async Task<Phone> PhoneAsync(string username)
    {
        var phone = new Phone(tokens => Api(tokens));
        _phones.Add(phone);
        var r = await phone.Accounts.RegisterAsync(username, $"{username}@example.com", DefaultPassword);
        Assert.True(r.Success, r.Error);
        return phone;
    }

    private readonly List<Phone> _phones = [];

    /// <summary>Registers <paramref name="username"/> (email username@example.com) and returns a signed-in client.</summary>
    protected async Task<SignedIn> SignUpAsync(string username, string password = DefaultPassword, string device = "test phone")
    {
        var response = await Client.PostAsJsonAsync("/v1/auth/register",
            new RegisterRequest(username, $"{username}@example.com", password, device));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        return new SignedIn(ClientWith(auth.Token), auth.Token, auth.User);
    }

    /// <summary>Signs in again as an existing user, as another device would.</summary>
    protected async Task<SignedIn> LogInAsync(string login, string password = DefaultPassword, string device = "other phone")
    {
        var response = await Client.PostAsJsonAsync("/v1/auth/login", new LoginRequest(login, password, device));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        return new SignedIn(ClientWith(auth.Token), auth.Token, auth.User);
    }

    /// <summary>Asserts the status and returns the ProblemDetails' detail.</summary>
    protected static async Task<string?> ProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        return problem?.Detail;
    }

    /// <summary>Extra service registrations for a test class, on top of the server's own.</summary>
    protected virtual void ConfigureServices(IServiceCollection services) { }

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseEnvironment("Testing")
            .UseSetting("ConnectionStrings:Social", postgres.ConnectionString($"test_{Guid.NewGuid():N}"))
            .UseSetting(ClientVersionCheck.SettingName, MinClientVersion)
            .ConfigureTestServices(services =>
            {
                services.AddSingleton<TimeProvider>(Clock);
                ConfigureServices(services);
            }));
        await using (var db = Db())
            await db.Database.MigrateAsync();
        Client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        foreach (var phone in _phones) phone.Dispose();
        await using (var db = Db())
            await db.Database.EnsureDeletedAsync();
        await _factory.DisposeAsync();
    }
}

/// <summary>A registered user and a client signed in as them.</summary>
public sealed record SignedIn(HttpClient Client, string Token, MeResponse User);

/// <summary>Real time, or <see cref="Fixed"/> when a test needs a known date (e.g. to cross a week boundary).</summary>
public sealed class TestClock : TimeProvider
{
    public DateTimeOffset? Fixed { get; set; }
    public override DateTimeOffset GetUtcNow() => Fixed ?? base.GetUtcNow();
}
