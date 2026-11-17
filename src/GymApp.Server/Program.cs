using GymApp.Contracts;
using GymApp.Server;
using GymApp.Server.Auth;
using GymApp.Server.Data;
using GymApp.Server.Email;
using GymApp.Server.Endpoints;
using GymApp.Server.Feedback;
using GymApp.Server.Friends;
using GymApp.Server.Push;
using GymApp.Social;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

// `dotnet GymApp.Server.dll migrate` applies the migrations and exits; production never migrates at startup.
// `suspend <username>`, `unsuspend <username>` and `push-test <username>` do their one thing and exit too
var migrateOnly = args is ["migrate"];
var isCommand = AccountCommands.IsCommand(args) || PushCommands.IsCommand(args);
var builder = WebApplication.CreateBuilder(migrateOnly || isCommand ? [] : args);

// Production reads its settings from environment variables only (ConnectionStrings__Social, AllowedHosts).
// A missing one is a message and exit code 1, not an exception: an unhandled one at startup can leave a container hanging.
var connection = builder.Configuration.GetConnectionString("Social");
var missing =
    string.IsNullOrWhiteSpace(connection) ? "Set ConnectionStrings:Social (ConnectionStrings__Social in production)."
    : !migrateOnly && !isCommand && builder.Environment.IsProduction() && string.IsNullOrWhiteSpace(builder.Configuration["AllowedHosts"])
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
// How emails go out (unset: to the log in Development, nowhere in production); half-set is a startup error too
EmailSettings? email = null;
try { email = EmailSettings.Read(builder.Configuration); }
catch (InvalidOperationException e) { missing ??= e.Message; }
// Invite links (Phase 21): all optional; a malformed value is a startup error too
InviteSettings? invites = null;
try { invites = InviteSettings.Read(builder.Configuration); }
catch (InvalidOperationException e) { missing ??= e.Message; }
// Push notifications (Phase 22): APNs and FCM each optional; half-set or a key that doesn't parse is a startup error too
PushSettings? push = null;
try { push = PushSettings.Read(builder.Configuration); }
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
builder.Services.AddSingleton<CheckInCleanup>();
builder.Services.AddSingleton<SignUpLimiter>();
builder.Services.AddSingleton(invites!);
// Friend notifications (Phase 22). A service without settings has no sender: its tokens are stored and nothing is sent.
// No request logging on either client: the APNs URL holds the device token
if (push!.Apns is { } apns)
{
    builder.Services.AddHttpClient(ApnsSender.HttpClientName, c =>
    {
        c.Timeout = TimeSpan.FromSeconds(5);
        c.DefaultRequestVersion = System.Net.HttpVersion.Version20;
        c.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
    }).RemoveAllLoggers();
    builder.Services.AddSingleton<IPushSender>(sp => ActivatorUtilities.CreateInstance<ApnsSender>(sp, apns));
}
if (push.Fcm is { } fcm)
{
    builder.Services.AddHttpClient(FcmSender.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(5)).RemoveAllLoggers();
    builder.Services.AddSingleton<IFcmAccessTokens>(new GoogleFcmAccessTokens(fcm));
    builder.Services.AddSingleton<IPushSender>(sp => ActivatorUtilities.CreateInstance<FcmSender>(sp, fcm));
}
builder.Services.AddSingleton(sp => ActivatorUtilities.CreateInstance<FriendNotifier>(sp));
if (email is not null) builder.Services.AddSingleton<IEmailSender>(sp => ActivatorUtilities.CreateInstance<SmtpEmailSender>(sp, email));
else if (builder.Environment.IsDevelopment()) builder.Services.AddSingleton<IEmailSender, LogEmailSender>();
else builder.Services.AddSingleton<IEmailSender, NoEmailSender>();
builder.Services.AddScoped<EmailCodeService>();
if (discordWebhook is not null)
{
    // No request logging: it would print the webhook URL, and the URL is the secret. The forwarder logs failures itself
    builder.Services.AddHttpClient(DiscordFeedbackForwarder.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(10))
        .RemoveAllLoggers();
    builder.Services.AddSingleton(sp => ActivatorUtilities.CreateInstance<DiscordFeedbackForwarder>(sp, discordWebhook));
}
builder.Services.AddHealthChecks().AddDbContextCheck<ServerDbContext>();
builder.Services.AddTrustedProxies(builder.Configuration);

builder.Services.AddAuthentication(SessionAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuthenticationHandler.SchemeName, null);
// Signed in unless an endpoint says otherwise (/health and /v1/auth register and login)
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

var app = builder.Build();

if (isCommand)
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
    var (ok, message) = PushCommands.IsCommand(args)
        ? await PushCommands.RunAsync(args, db, scope.ServiceProvider.GetRequiredService<FriendNotifier>())
        : await AccountCommands.RunAsync(args, db, TimeProvider.System);
    (ok ? Console.Out : Console.Error).WriteLine(message);
    Environment.ExitCode = ok ? 0 : 1;
    return;
}

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

// Temporary, for setting up ForwardedHeaders on a new host: logs the address the proxy connects from, the headers it
// sends and the client IP the server settles on. Off unless Diagnostics__LogProxyHeaders=true; it logs client IPs
if (app.Configuration.GetValue<bool>("Diagnostics:LogProxyHeaders"))
    app.Use(async (context, next) =>
    {
        var proxy = context.Connection.RemoteIpAddress;
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].ToString();
        var forwardedProto = context.Request.Headers["X-Forwarded-Proto"].ToString();
        await next();
        app.Logger.LogInformation("Proxy check: connection from {Proxy}, X-Forwarded-For '{ForwardedFor}', " +
            "X-Forwarded-Proto '{ForwardedProto}', client IP used {ClientIp}, scheme {Scheme}",
            proxy, forwardedFor, forwardedProto, context.Connection.RemoteIpAddress, context.Request.Scheme);
    });
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
app.MapReports();
app.MapShares();
app.MapInviteLinks();
app.MapPush();

app.Run();

/// <summary>Visible to the tests' WebApplicationFactory.</summary>
public partial class Program;
