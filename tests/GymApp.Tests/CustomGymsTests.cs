using GymApp.Core;

namespace GymApp.Tests;

public class CustomGymsTests
{
    [Theory]
    [InlineData("  Iron   Gym ", "Iron Gym")]
    [InlineData("Iron\tGym\n", "Iron Gym")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Clean_TrimsAndCollapsesSpaces(string? input, string expected) =>
        Assert.Equal(expected, CustomGyms.Clean(input));

    [Fact] public void EmptyName_IsRejected() => Assert.NotNull(CustomGyms.CheckName(CustomGyms.Clean("   ")));

    [Fact] public void OneCharacter_IsFine() => Assert.Null(CustomGyms.CheckName("X"));

    [Fact] public void SixtyCharacters_IsFine_SixtyOneIsNot()
    {
        Assert.Null(CustomGyms.CheckName(new string('a', 60)));
        Assert.NotNull(CustomGyms.CheckName(new string('a', 61)));
    }

    [Fact] public void Address_IsOptional_AndLimited()
    {
        Assert.Null(CustomGyms.CleanAddress("   "));
        Assert.Equal("Strada X 1", CustomGyms.CleanAddress(" Strada  X 1 "));
        Assert.Null(CustomGyms.CheckAddress(null));
        Assert.NotNull(CustomGyms.CheckAddress(new string('a', CustomGyms.MaxAddressLength + 1)));
    }

    [Theory]
    [InlineData("Home Gym", "home gym", true)]
    [InlineData("Știința", "știința", true)]     // SQLite NOCASE would miss this one
    [InlineData(" Home  Gym", "home gym", true)]
    [InlineData("Home Gym", "Home Gym 2", false)]
    public void SameName_IgnoresCaseAndSpacing(string a, string b, bool expected) =>
        Assert.Equal(expected, CustomGyms.SameName(a, b));
}
