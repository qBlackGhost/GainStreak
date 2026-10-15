using System.Text.Json;
using GymApp.Core;

namespace GymApp.Tests;

public class NominatimTests
{
    [Fact] public void Timisoara_ParsesNameIdCentreAndBox()
    {
        var c = Assert.Single(Nominatim.ParseSearch(Fixtures.Read("nominatim_timisoara.json")));
        Assert.Equal(OsmElementType.Relation, c.OsmType);
        Assert.Equal(6927733, c.OsmId);
        Assert.Equal("Timișoara", c.Name);
        Assert.Equal("Timișoara, Timiș, România", c.DisplayName);
        Assert.Equal(45.7538355, c.CenterLatitude, 7);
        Assert.Equal(21.2257474, c.CenterLongitude, 7);
        // Nominatim's box is [south, north, west, east]
        Assert.Equal(45.6838246, c.South, 7);
        Assert.Equal(45.8341735, c.North, 7);
        Assert.Equal(21.1116821, c.West, 7);
        Assert.Equal(21.3272446, c.East, 7);
    }

    [Fact] public void AmbiguousName_GivesSeveralCandidates_WithCountyInTheDisplayName()
    {
        var list = Nominatim.ParseSearch(Fixtures.Read("nominatim_sanmartin.json"));
        Assert.Equal(5, list.Count);
        Assert.Contains(list, c => c.DisplayName.Contains("Bihor"));
        Assert.Contains(list, c => c.DisplayName.Contains("Harghita"));
        Assert.Contains(list, c => c.OsmType == OsmElementType.Node);
        Assert.All(list, c => Assert.EndsWith("România", c.DisplayName));
    }

    [Fact] public void CityAndCountyWithTheSameName_OnlyTheCityIsKept()
    {
        // Real response for "Arad": the city (relation 10488682) and Arad county (relation 2248721)
        var c = Assert.Single(Nominatim.ParseSearch(Fixtures.Read("nominatim_arad.json")));
        Assert.Equal(10488682, c.OsmId);
        Assert.Equal("Arad", c.Name);
    }

    [Fact] public void Bucharest_IsKept_EvenThoughItHasACountysRank()
    {
        var c = Assert.Single(Nominatim.ParseSearch(Fixtures.Read("nominatim_bucuresti.json")));
        Assert.Equal("București", c.Name);
    }

    [Fact] public void TownPointAndItsBoundary_OnlyTheBoundaryIsKept()
    {
        // Real response for "Buziaș": the place node and the boundary relation
        var c = Assert.Single(Nominatim.ParseSearch(Fixtures.Read("nominatim_buzias.json")));
        Assert.Equal(OsmElementType.Relation, c.OsmType);
        Assert.Equal(10718301, c.OsmId);
    }

    [Fact] public void SameNamedVillageElsewhere_IsNotMergedIntoAnotherPlace()
    {
        // Sânmartin in Arad county is a node, but no "Sânmartin" boundary contains it, so it stays
        var list = Nominatim.ParseSearch(Fixtures.Read("nominatim_sanmartin.json"));
        Assert.Contains(list, c => c is { OsmType: OsmElementType.Node, OsmId: 422877931 });
    }

    [Theory]
    [InlineData("county")]
    [InlineData("state")]
    [InlineData("country")]
    [InlineData("suburb")]
    public void NonSettlements_AreDropped(string addressType) =>
        Assert.Empty(Nominatim.ParseSearch(
            $$"""[{"osm_type":"relation","osm_id":1,"addresstype":"{{addressType}}","lat":"1","lon":"1","name":"X","display_name":"X","boundingbox":["0","2","0","2"]}]"""));

    [Fact] public void EmptyResponse_GivesNoCandidates() =>
        Assert.Empty(Nominatim.ParseSearch(Fixtures.Read("nominatim_empty.json")));

    [Fact] public void BrokenEntries_AreSkipped_DuplicatesKeptOnce_NumbersAccepted()
    {
        var list = Nominatim.ParseSearch(Fixtures.Read("nominatim_messy.json"));
        Assert.Equal(2, list.Count);

        Assert.Equal("Lugoj", list[0].Name);   // empty name falls back to the first part of display_name
        Assert.Equal((OsmElementType.Relation, 1L), (list[0].OsmType, list[0].OsmId));

        Assert.Equal(OsmElementType.Way, list[1].OsmType);   // plain numbers instead of strings
        Assert.Equal(46.2, list[1].North);
    }

    [Fact] public void AtMostFiveCandidates()
    {
        var items = Enumerable.Range(1, 8).Select(i =>
            $$"""{"osm_type":"node","osm_id":{{i}},"lat":"1","lon":"1","name":"C{{i}}","display_name":"C{{i}}","boundingbox":["0","1","0","1"]}""");
        Assert.Equal(Nominatim.MaxCandidates, Nominatim.ParseSearch($"[{string.Join(",", items)}]").Count);
    }

    [Theory]
    [InlineData("{\"error\":\"nope\"}")]
    [InlineData("<html>Too many requests</html>")]
    public void NotAnArray_Throws(string body) =>
        Assert.ThrowsAny<JsonException>(() => Nominatim.ParseSearch(body));

    [Theory]
    [InlineData("node", OsmElementType.Node)]
    [InlineData("W", OsmElementType.Way)]
    [InlineData("relation", OsmElementType.Relation)]
    [InlineData("area", null)]
    [InlineData(null, null)]
    public void OsmType_Parses(string? text, OsmElementType? expected) =>
        Assert.Equal(expected, Nominatim.ParseOsmType(text));
}
