using GymApp.Core;

namespace GymApp.Tests;

public class NumberParsingTests
{
    [Theory]
    [InlineData("62.5", 62.5)]
    [InlineData("62,5", 62.5)]
    [InlineData(" 60 ", 60)]
    [InlineData("0", 0)]
    [InlineData(".5", 0.5)]
    public void Kg_Parses(string text, double expected)
    {
        Assert.True(NumberParsing.TryParseKg(text, out var kg));
        Assert.Equal(expected, kg);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("1e3")]
    [InlineData("6 0")]
    [InlineData("1.2.3")]
    public void Kg_RejectsBadInput(string? text) =>
        Assert.False(NumberParsing.TryParseKg(text, out _));

    [Theory]
    [InlineData("10", 10)]
    [InlineData(" 8 ", 8)]
    public void Reps_Parses(string text, int expected)
    {
        Assert.True(NumberParsing.TryParseReps(text, out var reps));
        Assert.Equal(expected, reps);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("x")]
    public void Reps_RejectsBadInput(string? text) =>
        Assert.False(NumberParsing.TryParseReps(text, out _));

    [Fact] public void FormatKg_DropsTrailingZeros()
    {
        Assert.Equal("60", NumberParsing.FormatKg(60));
        Assert.Equal("62.5", NumberParsing.FormatKg(62.5));
    }
}