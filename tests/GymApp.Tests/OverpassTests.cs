using System.Text.Json;
using GymApp.Core;

namespace GymApp.Tests;

public class OverpassTests
{
    private static IReadOnlyList<FetchedGym> Timisoara() => Overpass.ParseGyms(Fixtures.Read("overpass_timisoara.json"));

    [Fact] public void Timisoara_AllElementsBecomeGyms_NodesAndWays()
    {
        var gyms = Timisoara();
        Assert.Equal(21, gyms.Count);
        Assert.Equal(19, gyms.Count(g => g.OsmType == OsmElementType.Node));
        Assert.Equal(2, gyms.Count(g => g.OsmType == OsmElementType.Way));
    }

    [Fact] public void Node_UsesItsOwnPosition_AndBuildsTheAddress()
    {
        var g = Timisoara().Single(g => g.OsmId == 4179532756);
        Assert.Equal("Smartfit Studio 2", g.Name);
        Assert.Equal("Calea Buziașului 11", g.Address);
        Assert.Equal(45.7325049, g.Latitude, 7);
        Assert.Equal(21.2617934, g.Longitude, 7);
    }

    [Fact] public void Way_UsesItsCentre()
    {
        var g = Timisoara().Single(g => g.OsmId == 109687449);
        Assert.Equal(OsmElementType.Way, g.OsmType);
        Assert.Equal("Clubul Sportiv Timpolis", g.Name);
        Assert.Equal(45.7593991, g.Latitude, 7);
        Assert.Equal(21.2405377, g.Longitude, 7);
    }

    [Fact] public void StreetWithoutNumber_IsTheAddress_NoStreetMeansNoAddress()
    {
        var gyms = Timisoara();
        Assert.Equal("Strada Traian Lalescu", gyms.Single(g => g.OsmId == 4371190992).Address);
        Assert.Null(gyms.Single(g => g.OsmId == 4578303549).Address);
    }

    [Fact] public void NoName_BecomesUnnamedGym()
    {
        Assert.Equal(Overpass.UnnamedGym, Timisoara().Single(g => g.OsmId == 229395714).Name);
    }

    [Theory]
    [InlineData("Iron Gym", "Strada X", "Iron Gym")]
    [InlineData("  Iron Gym ", null, "Iron Gym")]
    [InlineData(null, "Strada X", "Unnamed gym (Strada X)")]
    [InlineData("  ", " Strada X ", "Unnamed gym (Strada X)")]
    [InlineData(null, null, "Unnamed gym")]
    public void Name_Rules(string? name, string? street, string expected) =>
        Assert.Equal(expected, Overpass.Name(name, street));

    [Theory]
    [InlineData("Strada X", "12", "Strada X 12")]
    [InlineData("Strada X", null, "Strada X")]
    [InlineData("Strada X", " ", "Strada X")]
    [InlineData(null, "12", null)]
    [InlineData(null, null, null)]
    public void Address_Rules(string? street, string? number, string? expected) =>
        Assert.Equal(expected, Overpass.Address(street, number));

    [Fact] public void BrokenElements_AreSkipped_DuplicatesKeptOnce()
    {
        const string json = """
            {"elements":[
              {"type":"node","id":1,"lat":45.1,"lon":21.1,"tags":{"name":"A"}},
              {"type":"node","id":1,"lat":45.1,"lon":21.1,"tags":{"name":"A again"}},
              {"type":"node","id":1,"lat":45.1,"lon":21.1},
              {"type":"way","id":1,"center":{"lat":45.2,"lon":21.2}},
              {"type":"way","id":2},
              {"type":"area","id":3,"lat":1,"lon":1},
              {"type":"node","lat":1,"lon":1},
              {"type":"relation","id":4,"center":{"lat":"x","lon":1}},
              "junk"
            ]}
            """;
        var gyms = Overpass.ParseGyms(json);
        Assert.Equal(2, gyms.Count);
        Assert.Equal("A", gyms[0].Name);
        Assert.Equal((OsmElementType.Way, 1L), (gyms[1].OsmType, gyms[1].OsmId));   // same id, other type: a different gym
        Assert.Equal(Overpass.UnnamedGym, gyms[1].Name);   // no tags at all
    }

    [Fact] public void NoElements_GivesNoGyms() =>
        Assert.Empty(Overpass.ParseGyms("""{"version":0.6,"elements":[]}"""));

    [Theory]
    [InlineData("[]")]
    [InlineData("<html>504 Gateway Timeout</html>")]
    public void NotAnOverpassResponse_Throws(string body) =>
        Assert.ThrowsAny<JsonException>(() => Overpass.ParseGyms(body));

    [Fact] public void Remark_Throws_EvenWithElements_SinceTheyMayBeCutShort()
    {
        const string json = """
            {"elements":[{"type":"node","id":1,"lat":1,"lon":1}],
             "remark":"runtime error: Query timed out in \"query\" at line 1 after 26 seconds."}
            """;
        var ex = Assert.Throws<OverpassRemarkException>(() => Overpass.ParseGyms(json));
        Assert.Contains("timed out", ex.Message);
    }

    [Fact] public void Query_UsesTheBox_InvariantCulture_AndOutCenter()
    {
        var q = Overpass.GymQuery(45.6838246, 21.1116821, 45.8341735, 21.3272446);
        Assert.Equal(
            "[out:json][timeout:25];nwr[\"leisure\"=\"fitness_centre\"](45.6838246,21.1116821,45.8341735,21.3272446);out center tags;",
            q);
    }
}
