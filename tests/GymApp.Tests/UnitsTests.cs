using GymApp.Core;

namespace GymApp.Tests;

public class UnitsTests
{
    [Fact] public void Pounds_RoundTrip() => Assert.Equal(80, Units.LbToKg(Units.KgToLb(80)), 9);

    [Fact] public void Kg_ToLb() => Assert.Equal(176.37, Units.KgToLb(80), 2);

    [Fact] public void FeetInches_ToCm() => Assert.Equal(180.34, Units.FeetInchesToCm(5, 11), 2);

    [Theory]
    [InlineData(180, 5, 11)]
    [InlineData(182.5, 6, 0)]     // 71.85 in rounds to 72, so 6 ft 0 in, never 5 ft 12 in
    [InlineData(152.4, 5, 0)]
    public void Cm_ToFeetInches(double cm, int feet, int inches) =>
        Assert.Equal((feet, inches), Units.CmToFeetInches(cm));

    [Fact] public void Formats()
    {
        Assert.Equal("80.5 kg", Units.FormatWeight(80.5, UnitSystem.Metric));
        Assert.Equal("176.4 lb", Units.FormatWeight(80, UnitSystem.Imperial));
        Assert.Equal("180 cm", Units.FormatHeight(180.2, UnitSystem.Metric));
        Assert.Equal("5 ft 11 in", Units.FormatHeight(180, UnitSystem.Imperial));
    }

    [Theory]
    [InlineData("8000", 8000)]
    [InlineData("8,000", 8000)]
    [InlineData("8.000", 8000)]
    [InlineData(" 8 000 ", 8000)]
    public void Steps_Parse(string text, int expected)
    {
        Assert.True(Units.TryParseSteps(text, out var steps));
        Assert.Equal(expected, steps);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-5")]
    public void Steps_RejectGarbage(string text) => Assert.False(Units.TryParseSteps(text, out _));
}
