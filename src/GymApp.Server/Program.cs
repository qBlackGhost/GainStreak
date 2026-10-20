using GymApp.Contracts;
using GymApp.Server;
using GymApp.Server.Auth;
using GymApp.Server.Data;
using GymApp.Server.Endpoints;
using GymApp.Server.Feedback;
using GymApp.Server.Friends;
using GymApp.Social;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

// `dotnet GymApp.Server.dll migrate` applies the migrations and exits; production never migrates at startup
var migrateOnly = args is ["migrate"];
var builder = WebApplication.CreateBuilder(migrateOnly ? [] : args);

// Production reads its settings from environment variables only (ConnectionStrings__Social, AllowedHosts).
// A missing one is a message and exit code 1, not an exception: an unhandled one at startup can leave a container hanging.
var connection = builder.Configuration.GetConnectionString("Social");
var missing =
    string.IsNullOrWhiteSpace(connection) ? "Set ConnectionStrings:Social (ConnectionStrings__Social in production)."
    : !migrateOnly && builder.Environment.IsProduction() && string.IsNullOrWhiteSpace(builder.Configuration["AllowedHosts"])
        ? "Set AllowedHosts to the server's host name(s), separated by ';'."
    : null;
// The oldest app build the API accepts (unset: any); a value that isn't a version is a startup error like the above
Version? minClientVersion = null;
try { minClientVersion = ClientVersionCheck.ReadMinimum(builder.Configuration); }
catch (InvalidOperationException e) { missing ??= e.Message; }
// Where feedback is forwarded (unset: only stored); a value that isn't an https URL is a startup error too
Uri? discordWebhook = null;
try { discordWebhook = DiscordFeedbackForwarder.ReadWebhook(builder.Configuration); }
catch (InvalidOperationException e) { missing ??= e.Message; }
if (missing is not null)
{
    Console.Error.WriteLine(missing);
    Environment.ExitCode = 1;
    return;
}

// A factory for the social store's own transactions, plus a context per request for everything else
builder.Services.AddDbContextFactory<ServerDbContext>(o => o.UseNpgsql(connection));
builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<ServerDbContext>>().CreateDbContext());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ISocialStore, PostgresSocialStore>();
builder.Services.AddScoped(sp => new FriendService(sp.GetRequiredService<ISocialStore>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddScoped(sp => new LeaderboardService(sp.GetRequiredService<ISocialStore>()));
builder.Services.ConfigureHttpJsonOptions(o => ApiFormat.Configure(o.SerializerOptions));
builder.Services.AddProblemDetails();
builder.Services.AddGymRateLimits();
builder.Services.AddHostedService<CheckInCleanup>();
builder.Services.AddSingleton<FeedbackQueue>();
if (discordWebhook is not null)
{
    builder.Services.AddHttpClient(DiscordFeedbackForwarder.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(15));
    builder.Services.AddSingleton(sp => ActivatorUtilities.CreateInstance<DiscordFeedbackForwarder>(sp, discordWebhook));
    builder.Services.AddHostedService(sp => sp.GetRequiredService<DiscordFeedbackForwarder>());
}
builder.Services.AddHealthChecks().AddDbContextCheck<ServerDbContext>();
builder.Services.AddTrustedProxies(builder.Configuration);

builder.Services.AddAuthentication(SessionAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuthenticationHandler.SchemeName, null);
// Signed in unless an endpoint says otherwise (/health and /v1/auth register and login)
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

var app = builder.Build();

if (migrateOnly || app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<ServerDbContext>().Database.MigrateAsync();
    if (migrateOnly)
    {
        app.Logger.LogInformation("Migrations applied.");
        return;
    }
}

// First, so everything after it (the rate limiter above all) sees the client's IP, not the proxy's
app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
// Before sign-in, so an old build gets "please update" even at login, not a 401 it would misread
app.UseClientVersionCheck(minClientVersion);
app.UseAuthentication();
app.UseAuthorization();
// After authentication, so the global limit can be per signed-in user
app.UseRateLimiter();

app.MapHealthChecks("/health").AllowAnonymous().DisableRateLimiting();
app.MapAccounts();
app.MapFriends();
app.MapStats();
app.MapCheckIns();
app.MapFeedback();

app.Run();

/// <summary>Visible to the tests' WebApplicationFactory.</summary>
public partial class Program;
