using System.Text.Json;
using GymApp.Core;

namespace GymApp.Tests;

public class AddressesTests
{
    [Theory]
    [InlineData("Calea Aradului", "1", "Circumvalațiunii", "Calea Aradului 1, Circumvalațiunii")]
    [InlineData("Strada X", null, "Elisabetin", "Strada X, Elisabetin")]
    [InlineData("Strada X", " 8-10 ", null, "Strada X 8-10")]
    [InlineData(" Strada X ", "", " ", "Strada X")]
    public void Format(string road, string? number, string? area, string expected) =>
        Assert.Equal(expected, Addresses.Format(road, number, area));

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("12", true)]
    [InlineData("8-10", true)]
    [InlineData("3A", true)]
    [InlineData("bis", false)]
    [InlineData("12345678901", false)]
    public void HouseNumber(string? number, bool ok) =>
        Assert.Equal(ok, Addresses.CheckHouseNumber(number) is null);

    [Fact] public void Reverse_WorldClass_GivesStreetAndSuburb() =>
        Assert.Equal("Piața Consiliul Europei, Lipovei",
            Addresses.ParseReverse(Fixtures.Read("nominatim_reverse_worldclass.json")));

    [Fact] public void Reverse_NothingThere_IsNull() =>
        Assert.Null(Addresses.ParseReverse(Fixtures.Read("nominatim_reverse_nothing.json")));

    [Fact] public void Reverse_NoStreet_IsNull() =>
        Assert.Null(Addresses.ParseReverse("""{"address":{"city":"Timișoara","suburb":"Lipovei"}}"""));

    [Fact] public void Reverse_WithHouseNumber_IncludesIt() =>
        Assert.Equal("Strada X 5, Fabric",
            Addresses.ParseReverse("""{"address":{"road":"Strada X","house_number":"5","quarter":"Fabric"}}"""));

    [Fact] public void Reverse_NotAnObject_Throws() =>
        Assert.ThrowsAny<JsonException>(() => Addresses.ParseReverse("[]"));

    [Fact] public void Search_SameAddressFromSeveralBuildings_IsKeptOnce()
    {
        // Real response: two buildings and an address node, all "Calea Aradului 1"
        var c = Assert.Single(Addresses.ParseSearch(Fixtures.Read("nominatim_address_calea_aradului.json")));
        Assert.Equal("Calea Aradului 1, Circumvalațiunii", c.Text);
        Assert.True(c.HasHouseNumber);
        Assert.Contains("Timișoara", c.DisplayName);
        Assert.InRange(c.Latitude, 45.68, 45.84);
    }

    [Fact] public void Search_StreetWithoutNumber_HasNoHouseNumber()
    {
        var c = Assert.Single(Addresses.ParseSearch(Fixtures.Read("nominatim_address_lalescu.json")));
        Assert.Equal("Strada Traian Lalescu, Elisabetin", c.Text);
        Assert.False(c.HasHouseNumber);
        Assert.Equal("Strada Traian Lalescu", c.Road);
        Assert.Equal("Elisabetin", c.Area);
    }

    [Fact] public void Search_SkipsResultsWithoutAStreetOrPosition()
    {
        const string json = """
            [
              {"lat":"45.7","lon":"21.2","address":{"suburb":"Lipovei"},"display_name":"Lipovei"},
              {"address":{"road":"Strada A"},"display_name":"A"},
              {"lat":"45.7","lon":"21.2","address":{"pedestrian":"Piața Unirii"},"display_name":"Piața Unirii, Timișoara"},
              "junk"
            ]
            """;
        var c = Assert.Single(Addresses.ParseSearch(json));
        Assert.Equal("Piața Unirii", c.Text);
    }

    [Fact] public void Search_NotAnArray_Throws() =>
        Assert.ThrowsAny<JsonException>(() => Addresses.ParseSearch("""{"error":"x"}"""));
}
