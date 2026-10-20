using System.Net;
using System.Net.Http.Json;
using GymApp.Client;
using GymApp.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace GymApp.Server.Tests;

/// <summary>A server that accepts 0.15.0 and newer.</summary>
public class ClientVersionTests(PostgresFixture postgres) : ServerTest(postgres)
{
    protected override string? MinClientVersion => "0.15.0";

    private async Task<HttpResponseMessage> LoginAsync(string? version)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest("alice", DefaultPassword, "phone"))
        };
        if (version is not null) request.Headers.Add(ClientVersions.Header, version);
        return await Client.SendAsync(request);
    }

    [Theory]
    [InlineData(null)]          // builds from before 0.15 send no version
    [InlineData("0.14.2")]
    [InlineData("not a version")]
    public async Task OldOrMissingVersion_Is426_WithThePleaseUpdateMessage(string? version)
    {
        var response = await LoginAsync(version);

        Assert.Equal(HttpStatusCode.UpgradeRequired, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(ClientVersionCheck.TooOldMessage, problem!.Detail);
        Assert.Equal("0.15.0", problem.Extensions[ClientVersions.MinVersionField]?.ToString());
    }

    [Theory]
    [InlineData("0.15.0")]
    [InlineData("0.15.0-beta")]
    [InlineData("0.16.1")]
    [InlineData("1.0.0")]
    public async Task CurrentOrNewerVersion_GetsThrough(string version)
    {
        var response = await LoginAsync(version);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);   // reached the endpoint: no such user
    }

    [Fact] public async Task Health_IsNotChecked()
    {
        var response = await Client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact] public async Task TheAppsClient_WithItsVersion_Works_AndAnOldOneIsToldToUpdate()
    {
        var current = Api(appVersion: "0.15.0");
        Assert.True((await current.RegisterAsync(new RegisterRequest("alice", "alice@example.com", DefaultPassword, "phone"))).IsOk);

        var old = Api(appVersion: "0.14.2");
        string? told = null;
        old.UpdateRequired += (_, message) => told = message;
        var r = await old.LoginAsync(new LoginRequest("alice", DefaultPassword, "phone"));

        Assert.True(r.IsUpdateRequired);
        Assert.Equal(ClientVersionCheck.TooOldMessage, told);
    }

    [Fact] public void ReadMinimum_UnsetIsNull_AndATypoThrows()
    {
        static IConfiguration Config(string? value) => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [ClientVersionCheck.SettingName] = value }).Build();

        Assert.Null(ClientVersionCheck.ReadMinimum(Config(null)));
        Assert.Equal(new Version(0, 15, 0), ClientVersionCheck.ReadMinimum(Config("0.15")));
        Assert.Throws<InvalidOperationException>(() => ClientVersionCheck.ReadMinimum(Config("0.15.x")));
    }
}
