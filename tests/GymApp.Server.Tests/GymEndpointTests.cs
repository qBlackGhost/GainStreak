using System.Net;
using System.Net.Http.Json;
using GymApp.Contracts;
using GymApp.Core;
using GymApp.Server.Data;
using GymApp.Server.Endpoints;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Server.Tests;

/// <summary>0.27.0: gyms from the server's copy of OSM, and the Geofabrik import that fills it.</summary>
public class GymEndpointTests : ServerTest
{
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
    private static readonly GeoBox Romania = new(43.61, 20.24, 48.27, 30.28);
    private const string Bucharest = "south=44.33&west=25.96&north=44.54&east=26.24";

    public GymEndpointTests(PostgresFixture postgres) : base(postgres) => Clock.Fixed = new DateTimeOffset(Now);

    private static FetchedGym Gym(long id, string name, double lat, double lon, OsmElementType type = OsmElementType.Node,
        string? address = null) => new(type, id, name, address, lat, lon);

    private Task<GymImportResult> ImportAsync(params FetchedGym[] gyms) => ImportAsync(Now.AddDays(-2), gyms);

    private async Task<GymImportResult> ImportAsync(DateTime asOf, params FetchedGym[] gyms)
    {
        await using var db = Db();
        return await GymImport.ApplyAsync(db, "romania", gyms, Romania, asOf, Now);
    }

    private static async Task<GymBoxResponse> GetAsync(HttpClient client, string box)
    {
        var response = await client.GetAsync($"/v1/gyms?{box}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GymBoxResponse>(ApiFormat.Json))!;
    }

    [Fact] public async Task ABox_ReturnsItsActiveGyms_AndSaysItIsCovered()
    {
        await ImportAsync(
            Gym(1, "World Class", 44.43, 26.10, address: "Calea Victoriei 1"),
            Gym(2, "18Gym", 46.76, 23.55),                                     // Cluj: outside the box
            Gym(3, "Iron Box", 44.45, 26.05, OsmElementType.Way));
        var me = await SignUpAsync("alice");

        var r = await GetAsync(me.Client, Bucharest);
        Assert.True(r.Covered);
        Assert.Equal(Now.AddDays(-2), r.DataAsOf);
        Assert.Equal([(OsmType.Node, 1L, "World Class", "Calea Victoriei 1"), (OsmType.Way, 3L, "Iron Box", null)],
            r.Gyms.Select(g => (g.OsmType, g.OsmId, g.Name, g.Address)));
    }

    [Fact] public async Task OutsideALoadedCountry_OrWithOldData_ItIsNotCovered()
    {
        var me = await SignUpAsync("alice");
        var vienna = "south=48.1&west=16.2&north=48.3&east=16.5";
        Assert.False((await GetAsync(me.Client, vienna)).Covered);   // nothing loaded at all

        await ImportAsync(Gym(1, "World Class", 44.43, 26.10));
        var r = await GetAsync(me.Client, vienna);
        Assert.False(r.Covered);
        Assert.Null(r.DataAsOf);
        Assert.Empty(r.Gyms);

        await ImportAsync(Now.AddDays(-(GymEndpoints.FreshDays + 1)), Gym(1, "World Class", 44.43, 26.10));
        r = await GetAsync(me.Client, Bucharest);
        Assert.False(r.Covered);           // the weekly import stopped: let the app ask Overpass
        Assert.Single(r.Gyms);
    }

    [Fact] public async Task ACoveredBoxWithNoGyms_IsAnEmptyCoveredAnswer()
    {
        await ImportAsync(Gym(1, "World Class", 44.43, 26.10));
        var me = await SignUpAsync("alice");
        var r = await GetAsync(me.Client, "south=45.0&west=24.0&north=45.05&east=24.05");   // a village in the Carpathians
        Assert.True(r.Covered);
        Assert.Empty(r.Gyms);
    }

    [Theory]
    [InlineData("south=44.5&west=26.0&north=44.3&east=26.2")]     // inverted
    [InlineData("south=44&west=25&north=47&east=26")]             // 3 degrees tall
    [InlineData("south=-91&west=25&north=-90&east=26")]           // off the map
    [InlineData("west=25&north=45&east=26")]                      // south missing
    public async Task ABadBox_IsRefused(string box)
    {
        var me = await SignUpAsync("alice");
        Assert.Equal(HttpStatusCode.BadRequest, (await me.Client.GetAsync($"/v1/gyms?{box}")).StatusCode);
    }

    [Fact] public async Task ItNeedsASignedInUser() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync($"/v1/gyms?{Bucharest}")).StatusCode);

    [Fact] public async Task TheAppsClient_ReadsIt()
    {
        await ImportAsync(Gym(1, "World Class", 44.43, 26.10));
        var phone = await PhoneAsync("alice");
        var r = await phone.Api.GetGymsAsync(44.33, 25.96, 44.54, 26.24);
        Assert.True(r.IsOk, r.Error);
        Assert.Equal("World Class", Assert.Single(r.Value!.Gyms).Name);
    }

    // ------------------------------------------------------------ the import

    [Fact] public async Task AnImport_Adds_Updates_AndDeactivatesWhatIsGone_ThenBringsItBack()
    {
        var first = await ImportAsync(Gym(1, "Gold gym", 44.43, 26.10), Gym(2, "Spartan", 44.44, 26.11));
        Assert.Equal((2, 0, 0, 2), (first.Added, first.Updated, first.Deactivated, first.Total));

        var second = await ImportAsync(Gym(1, "Gold Gym Unirii", 44.43, 26.10), Gym(3, "Body Factory", 44.45, 26.12));
        Assert.Equal((1, 1, 1, 2), (second.Added, second.Updated, second.Deactivated, second.Total));

        await using (var db = Db())
        {
            var rows = await db.OsmGyms.AsNoTracking().OrderBy(g => g.OsmId).ToListAsync();
            Assert.Equal([(1L, "Gold Gym Unirii", true), (2L, "Spartan", false), (3L, "Body Factory", true)],
                rows.Select(g => (g.OsmId, g.Name, g.IsActive)));
            var c = await db.OsmGymCountries.SingleAsync();
            Assert.Equal(("romania", 2, Romania.North), (c.Country, c.GymCount, c.North));
        }

        var third = await ImportAsync(Gym(1, "Gold Gym Unirii", 44.43, 26.10), Gym(2, "Spartan", 44.44, 26.11), Gym(3, "Body Factory", 44.45, 26.12));
        Assert.Equal((0, 1, 0), (third.Added, third.Updated, third.Deactivated));   // Spartan is back
        var me = await SignUpAsync("alice");
        Assert.Equal(3, (await GetAsync(me.Client, Bucharest)).Gyms.Count);
    }

    [Fact] public async Task AFailedImport_LeavesTheOldRows()
    {
        await ImportAsync(Gym(1, "Gold gym", 44.43, 26.10));
        await using (var db = Db())
        {
            var tooLong = new string('x', OsmGym.MaxCountryLength + 1);   // the country column can't hold it: the save fails
            await Assert.ThrowsAnyAsync<DbUpdateException>(() =>
                GymImport.ApplyAsync(db, tooLong, [Gym(9, "New", 44.4, 26.1)], Romania, Now, Now));
        }
        await using (var db = Db())
        {
            Assert.Equal(["Gold gym"], await db.OsmGyms.Select(g => g.Name).ToListAsync());
            Assert.Equal(["romania"], await db.OsmGymCountries.Select(c => c.Country).ToListAsync());
        }
    }

    [Fact] public void TheGeoJson_GivesWaysTheirCentre_KeepsEachFeatureOnce_AndNamesLikeOverpass()
    {
        const string json = """
            {"type":"FeatureCollection","features":[
            {"type":"Feature","geometry":{"type":"Point","coordinates":[23.5535938,46.7613904]},
             "properties":{"@type":"node","@id":1631997565,"addr:housenumber":"4","addr:street":"Strada Tășnad","leisure":"fitness_centre","name":"18Gym"}},
            {"type":"Feature","geometry":{"type":"LineString","coordinates":[[26.0,44.0],[26.2,44.0],[26.2,44.4],[26.0,44.0]]},
             "properties":{"@type":"way","@id":42,"addr:street":"Bulevardul Unirii","leisure":"fitness_centre"}},
            {"type":"Feature","geometry":{"type":"MultiPolygon","coordinates":[[[[26.0,44.0],[26.2,44.0],[26.2,44.4],[26.0,44.0]]]]},
             "properties":{"@type":"way","@id":42,"addr:street":"Bulevardul Unirii","leisure":"fitness_centre"}},
            {"type":"Feature","geometry":{"type":"Point","coordinates":[26.1,44.4]},"properties":{"leisure":"fitness_centre"}}
            ]}
            """;
        var gyms = GymImport.ParseGeoJson(json);
        Assert.Equal(2, gyms.Count);
        Assert.Equal(new FetchedGym(OsmElementType.Node, 1631997565, "18Gym", "Strada Tășnad 4", 46.7613904, 23.5535938), gyms[0]);
        Assert.Equal((OsmElementType.Way, 42L, "Unnamed gym (Bulevardul Unirii)", "Bulevardul Unirii"),
            (gyms[1].OsmType, gyms[1].OsmId, gyms[1].Name, gyms[1].Address));
        Assert.Equal((44.2, 26.1), (Math.Round(gyms[1].Latitude, 6), Math.Round(gyms[1].Longitude, 6)));
    }

    [Fact] public void TheFileInfo_GivesTheCountrysBoxAndTheDataDate()
    {
        const string json = """
            {"header":{"boxes":[[20.24181,43.612466,30.27896,48.269483]],
             "option":{"osmosis_replication_timestamp":"2026-11-21T21:20:51Z","timestamp":"2026-11-21T21:20:51Z"}}}
            """;
        var (box, asOf) = GymImport.ParseFileInfo(json);
        Assert.Equal(new GeoBox(43.612466, 20.24181, 48.269483, 30.27896), box);
        Assert.Equal(new DateTime(2026, 11, 21, 21, 20, 51, DateTimeKind.Utc), asOf);
        Assert.Equal((null, null), GymImport.ParseFileInfo("""{"header":{}}"""));
    }
}
