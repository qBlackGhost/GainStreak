using System.Net;

namespace GymApp.Server.Tests;

public class HealthTests(PostgresFixture postgres) : ServerTest(postgres)
{
    [Fact] public async Task Health_IsOk()
    {
        var response = await Client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
